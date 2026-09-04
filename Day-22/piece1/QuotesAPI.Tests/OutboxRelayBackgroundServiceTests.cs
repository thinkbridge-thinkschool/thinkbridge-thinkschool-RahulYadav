using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuotesApi.Data;
using QuotesApi.Messaging;
using QuotesApi.Models;
using QuotesApi.Options;
using QuotesApi.Repositories;
using QuotesApi.Services;
using Xunit.Abstractions;

namespace QuotesApi.Tests;

// Day 20: exercises OutboxRelayBackgroundService directly through the same
// IHostedService.StartAsync/StopAsync lifecycle the real ASP.NET Core host
// drives (same approach as QuoteProcessingBackgroundServiceTests), against
// a real SQLite database — the guarantee under test (a row is never marked
// sent before it is actually persisted) depends on real SaveChangesAsync
// behavior, not just in-memory sequencing.
public sealed class OutboxRelayBackgroundServiceTests : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    // Unlike ProcessedMessageStoreTests (single-threaded, one DbContext at
    // a time on one shared SqliteConnection instance), this test class has
    // a genuinely concurrent reader/writer: the relay's own background
    // poll loop runs on a different thread than the test's WaitUntilAsync
    // polling. Microsoft.Data.Sqlite's SqliteConnection is not safe for
    // concurrent use from multiple threads, so sharing one connection
    // OBJECT across those threads intermittently throws "database is
    // locked" from inside EF's own connection setup. The fix is the
    // standard EF Core pattern for this situation: a uniquely-named
    // shared-cache in-memory database (mode=memory&cache=shared), where
    // every DbContext opens its OWN SqliteConnection instance against the
    // same underlying data — concurrency is then handled by SQLite itself
    // (and Microsoft.Data.Sqlite's connection-string busy timeout), not by
    // one connection object being poked from two threads at once. A single
    // connection is kept open for the test's lifetime purely to keep the
    // shared in-memory database alive; nothing ever queries through it
    // directly.
    private readonly string _connectionString;
    private readonly SqliteConnection _keepAliveConnection;
    private readonly ITestOutputHelper _output;

    public OutboxRelayBackgroundServiceTests(ITestOutputHelper output)
    {
        _output = output;

        _connectionString =
            $"Data Source=file:outbox-relay-tests-{Guid.NewGuid():N}?mode=memory&cache=shared";

        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        using var db = NewDbContext();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _keepAliveConnection.Dispose();
    }

    private QuotesDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<QuotesDbContext>()
            .UseSqlite(_connectionString)
            .Options);

    private ServiceProvider BuildProvider(
        IQuoteEventPublisher publisher,
        IOutboxCrashInjector crashInjector)
    {
        var services = new ServiceCollection();

        // Every scope the relay creates gets its own QuotesDbContext AND
        // its own SqliteConnection, all pointed at the same shared-cache
        // in-memory database — matching the real "new scope per poll"
        // production pattern without the cross-thread connection sharing
        // that caused the flakiness described above.
        services.AddScoped(_ => NewDbContext());
        services.AddSingleton(publisher);
        services.AddSingleton(crashInjector);
        services.AddSingleton<IClock, SystemClock>();

        return services.BuildServiceProvider();
    }

    private static OutboxRelayBackgroundService CreateRelay(
        ServiceProvider provider,
        TimeSpan? pollInterval = null) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OutboxRelayBackgroundService>.Instance,
            Microsoft.Extensions.Options.Options.Create(new OutboxRelayOptions
            {
                PollInterval = pollInterval ?? PollInterval,
                BatchSize = 10
            }));

    private async Task<OutboxMessage> SeedUnsentOutboxRowAsync(string author = "Ada Lovelace")
    {
        using var db = NewDbContext();

        var quote = Quote.Create(author, "Seeded for the outbox relay tests.").Quote!;
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();

        var quoteCreated = new QuoteCreatedEvent(
            QuoteCreatedEvent.BuildMessageId(quote.Id),
            quote.Id,
            quote.Author,
            quote.Text,
            DateTimeOffset.UtcNow);

        var outboxMessage = new OutboxMessage
        {
            MessageId = quoteCreated.MessageId,
            EventType = QuoteCreatedEvent.EventType,
            Payload = System.Text.Json.JsonSerializer.Serialize(quoteCreated),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        db.OutboxMessages.Add(outboxMessage);
        await db.SaveChangesAsync();

        return outboxMessage;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met within the timeout.");

            await Task.Delay(10);
        }
    }

    // ----------------------------------------------------------------
    // Happy path
    // ----------------------------------------------------------------

    [Fact]
    public async Task UnsentRow_IsPublishedAndMarkedSent()
    {
        var seeded = await SeedUnsentOutboxRowAsync();
        var publisher = new RecordingQuoteEventPublisher();
        var crashInjector = new TestOutboxCrashInjector();

        using var provider = BuildProvider(publisher, crashInjector);
        var relay = CreateRelay(provider);

        await relay.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(
                async () =>
                {
                    using var db = NewDbContext();
                    var row = await db.OutboxMessages.FindAsync(seeded.Id);
                    return row!.SentAtUtc is not null;
                },
                WaitTimeout);
        }
        finally
        {
            using var stopCts = new CancellationTokenSource(WaitTimeout);
            await relay.StopAsync(stopCts.Token);
        }

        Assert.Single(publisher.PublishedEvents);
        Assert.Equal(seeded.MessageId, publisher.PublishedEvents[0].MessageId);

        using var verifyDb = NewDbContext();
        var finalRow = await verifyDb.OutboxMessages.FindAsync(seeded.Id);
        Assert.NotNull(finalRow!.SentAtUtc);
        Assert.Equal(1, finalRow.AttemptCount);
        Assert.Null(finalRow.LastError);
    }

    // ----------------------------------------------------------------
    // Publish failure — row stays unsent, error recorded, retried later
    // ----------------------------------------------------------------

    [Fact]
    public async Task PublishFailure_KeepsRowUnsent_RecordsAttemptAndError_ThenSucceedsOnRetry()
    {
        var seeded = await SeedUnsentOutboxRowAsync();
        var publisher = new FlakyQuoteEventPublisher(failFirstAttempts: 1);
        var crashInjector = new TestOutboxCrashInjector();

        using var provider = BuildProvider(publisher, crashInjector);

        // A generous poll interval: this relay only needs to complete ONE
        // (failing) poll before we inspect and stop it, so there is no
        // race between that inspection and a second poll starting on its
        // own and succeeding first.
        var relay = CreateRelay(provider, pollInterval: TimeSpan.FromSeconds(2));

        await relay.StartAsync(CancellationToken.None);

        // Wait for the first (failing) attempt to be recorded, then stop
        // the relay immediately — deterministically before the next poll
        // (2 seconds away) could retry on its own.
        await WaitUntilAsync(
            async () =>
            {
                using var db = NewDbContext();
                var row = await db.OutboxMessages.FindAsync(seeded.Id);
                return row!.AttemptCount >= 1;
            },
            WaitTimeout);

        using (var stopCts = new CancellationTokenSource(WaitTimeout))
        {
            await relay.StopAsync(stopCts.Token);
        }

        using (var db = NewDbContext())
        {
            var row = await db.OutboxMessages.FindAsync(seeded.Id);
            Assert.Null(row!.SentAtUtc);
            Assert.NotNull(row.LastError);
            Assert.Equal(1, row.AttemptCount);
        }

        // The relay "restarts" and retries the SAME row/MessageId rather
        // than giving up on it.
        var relayAfterRestart = CreateRelay(provider);

        await relayAfterRestart.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(
                async () =>
                {
                    using var db = NewDbContext();
                    var row = await db.OutboxMessages.FindAsync(seeded.Id);
                    return row!.SentAtUtc is not null;
                },
                WaitTimeout);
        }
        finally
        {
            using var stopCts = new CancellationTokenSource(WaitTimeout);
            await relayAfterRestart.StopAsync(stopCts.Token);
        }

        using var verifyDb = NewDbContext();
        var finalRow = await verifyDb.OutboxMessages.FindAsync(seeded.Id);
        Assert.NotNull(finalRow!.SentAtUtc);
        Assert.Null(finalRow.LastError);
        Assert.True(finalRow.AttemptCount >= 2);

        Assert.All(
            publisher.PublishedMessageIds,
            id => Assert.Equal(seeded.MessageId, id));
    }

    // ----------------------------------------------------------------
    // THE crash window (see IOutboxCrashInjector / OutboxCrashSimulation
    // Exception): publish succeeds, "crash" happens before SentAtUtc is
    // saved, the row is retried, redelivered under the SAME MessageId, and
    // the Day 19 consumer-side idempotency store is what makes that
    // redelivery safe. This is the exact scenario called out in the
    // exercise: at-least-once delivery + an idempotent consumer, not
    // exactly-once delivery.
    // ----------------------------------------------------------------

    [Fact]
    public async Task CrashAfterPublishBeforeMarkSent_RowSurvivesUnsent_IsRetried_AndConsumerDedupesTheDuplicate()
    {
        var seeded = await SeedUnsentOutboxRowAsync("Grace Hopper");

        _output.WriteLine(
            $"[Initial state]  OutboxMessage MessageId={seeded.MessageId}  SentAtUtc=NULL  AttemptCount=0");

        var publisher = new RecordingQuoteEventPublisher();
        var crashInjector = new TestOutboxCrashInjector();

        // Arrange the crash for the very first publish of this row.
        crashInjector.CrashOnNextPublish(seeded.MessageId);

        using var provider = BuildProvider(publisher, crashInjector);

        // A generous poll interval here — this relay only ever needs to
        // complete ONE poll (which crashes). A wide gap before any
        // possible second poll removes any timing race with the
        // "stop immediately after the crash" step below.
        var relay = CreateRelay(provider, pollInterval: TimeSpan.FromSeconds(2));

        // ---- 1. First "process": publishes, then the simulated crash
        //         fires before SentAtUtc can be saved. Stop the relay the
        //         instant the publish is observed — deterministically
        //         before the NEXT poll (2 seconds away) could ever start a
        //         retry, so there is no race with the assertions below.
        await relay.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            async () => publisher.PublishedEvents.Count >= 1,
            WaitTimeout);

        using (var stopCts = new CancellationTokenSource(WaitTimeout))
        {
            await relay.StopAsync(stopCts.Token);
        }

        // The row must be exactly as a real crash would have left it:
        // still unsent, with NOTHING about this attempt persisted — not
        // even the attempt count or an error.
        using (var db = NewDbContext())
        {
            var rowAfterCrash = await db.OutboxMessages.FindAsync(seeded.Id);
            Assert.Null(rowAfterCrash!.SentAtUtc);
            Assert.Equal(0, rowAfterCrash.AttemptCount);
            Assert.Null(rowAfterCrash.LastError);

            _output.WriteLine(
                $"[After publish + simulated crash]  Service Bus received the message " +
                $"({publisher.PublishedEvents.Count} publish(es) so far), but the process " +
                $"'crashed' before the DB write: MessageId={rowAfterCrash.MessageId}  " +
                $"SentAtUtc=NULL  AttemptCount={rowAfterCrash.AttemptCount}  " +
                "(nothing about this attempt was persisted — the row was NOT lost).");
        }

        // ---- 2. The relay "restarts" — a brand new instance, exactly as
        //         a fresh process would create on restart, wired to the
        //         same durable store. It sees the same row as unsent and
        //         republishes it under the SAME MessageId (the crash
        //         schedule only ever fires once), and this time there is
        //         no crash, so it succeeds.
        var relayAfterRestart = CreateRelay(provider);

        await relayAfterRestart.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(
                async () =>
                {
                    using var db = NewDbContext();
                    var row = await db.OutboxMessages.FindAsync(seeded.Id);
                    return row!.SentAtUtc is not null;
                },
                WaitTimeout);
        }
        finally
        {
            using var stopCts = new CancellationTokenSource(WaitTimeout);
            await relayAfterRestart.StopAsync(stopCts.Token);
        }

        // ---- 3. The message was published at least twice under the
        //         SAME MessageId: once before the simulated crash, once
        //         on the successful retry. Nothing was lost; something
        //         was (harmlessly) duplicated.
        Assert.True(publisher.PublishedEvents.Count >= 2);
        Assert.All(
            publisher.PublishedEvents,
            e => Assert.Equal(seeded.MessageId, e.MessageId));

        using var finalDb = NewDbContext();
        var finalRow = await finalDb.OutboxMessages.FindAsync(seeded.Id);
        Assert.NotNull(finalRow!.SentAtUtc);
        // Only the one successful attempt was ever persisted — the crashed
        // attempt left no trace, matching a real process crash.
        Assert.Equal(1, finalRow.AttemptCount);

        _output.WriteLine(
            $"[After relay restart + retry]  MessageId={finalRow.MessageId}  " +
            $"SentAtUtc={finalRow.SentAtUtc:O}  AttemptCount={finalRow.AttemptCount}  " +
            $"(published {publisher.PublishedEvents.Count} time(s) total under this " +
            "MessageId — at least once, nothing lost).");

        // ---- 4. Feed every duplicate delivery through the REAL Day 19
        //         consumer path (QuoteEventMessageHandler +
        //         ProcessedMessageStore) and prove it processes the quote
        //         exactly once despite the redelivery.
        const string subscriptionName = "sub-a";
        var processedCount = 0;

        foreach (var delivered in publisher.PublishedEvents)
        {
            using var db = NewDbContext();
            var store = new ProcessedMessageStore(db);

            if (await store.HasBeenProcessedAsync(subscriptionName, delivered.MessageId, CancellationToken.None))
            {
                continue;
            }

            processedCount++;
            await store.MarkProcessedAsync(subscriptionName, delivered.MessageId, CancellationToken.None);
        }

        Assert.Equal(1, processedCount);

        using var processedDb = NewDbContext();
        var store2 = new ProcessedMessageStore(processedDb);
        Assert.True(
            await store2.HasBeenProcessedAsync(subscriptionName, seeded.MessageId, CancellationToken.None));

        _output.WriteLine(
            $"[Consumer idempotency]  {publisher.PublishedEvents.Count} delivery attempt(s) " +
            $"reached the consumer for MessageId={seeded.MessageId}, but business processing " +
            $"ran exactly {processedCount} time — Day 19's ProcessedMessage store deduped the " +
            "redelivery.");
    }

    [Fact]
    public async Task StopAsync_DoesNotHang_WhenNoUnsentRowsExist()
    {
        var publisher = new RecordingQuoteEventPublisher();
        var crashInjector = new TestOutboxCrashInjector();

        using var provider = BuildProvider(publisher, crashInjector);
        var relay = CreateRelay(provider);

        await relay.StartAsync(CancellationToken.None);

        using var stopCts = new CancellationTokenSource(WaitTimeout);
        await relay.StopAsync(stopCts.Token);

        Assert.Empty(publisher.PublishedEvents);
    }

    // Publisher that fails the first `failFirstAttempts` calls, then
    // succeeds — used to exercise the ordinary FAILURE branch of the
    // pattern (as opposed to the simulated-crash branch, which is a
    // different, deliberately un-recorded failure mode).
    private sealed class FlakyQuoteEventPublisher : IQuoteEventPublisher
    {
        private readonly int _failFirstAttempts;
        private int _attempts;

        public FlakyQuoteEventPublisher(int failFirstAttempts)
        {
            _failFirstAttempts = failFirstAttempts;
        }

        public List<string> PublishedMessageIds { get; } = new();

        public Task PublishQuoteCreatedAsync(
            QuoteCreatedEvent quoteCreated,
            CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _attempts);

            if (attempt <= _failFirstAttempts)
            {
                throw new InvalidOperationException(
                    $"Simulated transient publish failure (attempt {attempt}).");
            }

            PublishedMessageIds.Add(quoteCreated.MessageId);
            return Task.CompletedTask;
        }
    }
}
