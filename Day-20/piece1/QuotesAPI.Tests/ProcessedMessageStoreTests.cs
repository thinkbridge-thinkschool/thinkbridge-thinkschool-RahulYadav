using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Repositories;

namespace QuotesApi.Tests;

// Exercises the Day 19 idempotency store (see
// QuotesApi/Repositories/ProcessedMessageStore.cs) against a real SQLite
// database rather than a fake, because the behavior that matters here —
// per-subscription dedup, and two competing consumers safely racing to
// record the same MessageId — depends on the database's own unique
// constraint, not just in-memory logic.
public sealed class ProcessedMessageStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<QuotesDbContext> _options;
    private readonly QuotesDbContext _db;
    private readonly ProcessedMessageStore _store;

    public ProcessedMessageStoreTests()
    {
        // A single open SQLite in-memory connection, shared by every
        // DbContext created in a test — the in-memory database is dropped
        // as soon as its one connection closes, and this lets a test open
        // a second, independent DbContext against the same data to model
        // two separate competing-consumer worker instances.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<QuotesDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new QuotesDbContext(_options);
        _db.Database.EnsureCreated();

        _store = new ProcessedMessageStore(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task HasBeenProcessedAsync_ReturnsFalse_ForUnseenMessage()
    {
        var alreadyProcessed = await _store.HasBeenProcessedAsync(
            "sub-a", "msg-1", CancellationToken.None);

        Assert.False(alreadyProcessed);
    }

    [Fact]
    public async Task MarkProcessedAsync_ThenHasBeenProcessedAsync_ReturnsTrue()
    {
        await _store.MarkProcessedAsync("sub-a", "msg-1", CancellationToken.None);

        Assert.True(
            await _store.HasBeenProcessedAsync("sub-a", "msg-1", CancellationToken.None));
    }

    [Fact]
    public async Task HasBeenProcessedAsync_IsScopedPerSubscription()
    {
        await _store.MarkProcessedAsync("sub-a", "msg-1", CancellationToken.None);

        // Subscription B is an independent copy of the topic message and
        // has never seen this MessageId — it must process it on its own,
        // even though Subscription A already has.
        Assert.False(
            await _store.HasBeenProcessedAsync("sub-b", "msg-1", CancellationToken.None));
    }

    [Fact]
    public async Task MarkProcessedAsync_TwoCompetingConsumersRaceOnSameMessage_SecondCallDoesNotThrow()
    {
        // Models Worker-A1 and Worker-A2 (see ServiceBusSubscriptionWorker)
        // both passing the "not processed yet" check for the same message
        // before either finishes — a real race between competing
        // consumers on the same subscription. Each worker gets its own
        // DbContext (a new DI scope per message in production); the
        // shared connection here keeps them pointed at the same database.
        using var dbForWorkerA1 = new QuotesDbContext(_options);
        using var dbForWorkerA2 = new QuotesDbContext(_options);

        var storeForWorkerA1 = new ProcessedMessageStore(dbForWorkerA1);
        var storeForWorkerA2 = new ProcessedMessageStore(dbForWorkerA2);

        await storeForWorkerA1.MarkProcessedAsync("sub-a", "msg-1", CancellationToken.None);

        // Worker A2 lost the race at the database level but must not
        // crash or duplicate the record — losing gracefully is the point.
        await storeForWorkerA2.MarkProcessedAsync("sub-a", "msg-1", CancellationToken.None);

        Assert.True(
            await _store.HasBeenProcessedAsync("sub-a", "msg-1", CancellationToken.None));
    }
}
