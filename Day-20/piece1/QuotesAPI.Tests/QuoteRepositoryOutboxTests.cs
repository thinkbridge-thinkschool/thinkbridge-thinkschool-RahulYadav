using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Messaging;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Tests;

// Day 20: proves the Transactional Outbox write itself — the quote and its
// OutboxMessage row must land together, atomically, inside QuoteRepository.
// AddAsync's explicit EF Core transaction (see QuoteRepository.cs). A real
// SQLite database is used (not a fake) because the guarantee under test is
// a database-transaction property, not just in-memory sequencing.
public sealed class QuoteRepositoryOutboxTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<QuotesDbContext> _options;

    public QuoteRepositoryOutboxTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<QuotesDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new QuotesDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task AddAsync_WritesQuoteAndOutboxRow_InTheSameCommit()
    {
        using var db = new QuotesDbContext(_options);
        var repository = new QuoteRepository(db);

        var quote = Quote.Create("Ada Lovelace", "Atomicity matters.").Quote!;

        var created = await repository.AddAsync(
            quote,
            q => new OutboxMessage
            {
                MessageId = QuoteCreatedEvent.BuildMessageId(q.Id),
                EventType = QuoteCreatedEvent.EventType,
                Payload = System.Text.Json.JsonSerializer.Serialize(
                    new QuoteCreatedEvent(
                        QuoteCreatedEvent.BuildMessageId(q.Id),
                        q.Id,
                        q.Author,
                        q.Text,
                        DateTimeOffset.UtcNow)),
                CreatedAtUtc = DateTimeOffset.UtcNow
            },
            CancellationToken.None);

        // Read back through an independent DbContext (same underlying
        // connection) to confirm both rows are actually durable, not just
        // present in the first context's change tracker.
        using var verifyDb = new QuotesDbContext(_options);

        var persistedQuote = await verifyDb.Quotes.SingleAsync(q => q.Id == created.Id);
        Assert.Equal("Ada Lovelace", persistedQuote.Author);

        var outboxRow = await verifyDb.OutboxMessages.SingleAsync(
            x => x.MessageId == QuoteCreatedEvent.BuildMessageId(created.Id));

        Assert.Equal(QuoteCreatedEvent.EventType, outboxRow.EventType);
        Assert.Null(outboxRow.SentAtUtc);
        Assert.Equal(0, outboxRow.AttemptCount);
        Assert.Null(outboxRow.LastError);
    }

    [Fact]
    public async Task AddAsync_OutboxMessageFactory_ReceivesQuoteWithAssignedId()
    {
        // The whole point of factoring the outbox row via a delegate rather
        // than building it before the call: the quote has no Id until the
        // repository saves it inside the transaction. This proves the
        // factory sees the real, assigned Id, not the pre-insert default.
        using var db = new QuotesDbContext(_options);
        var repository = new QuoteRepository(db);

        var quote = Quote.Create("Grace Hopper", "Ids matter.").Quote!;

        int? idSeenByFactory = null;

        await repository.AddAsync(
            quote,
            q =>
            {
                idSeenByFactory = q.Id;
                return new OutboxMessage
                {
                    MessageId = QuoteCreatedEvent.BuildMessageId(q.Id),
                    EventType = QuoteCreatedEvent.EventType,
                    Payload = "{}",
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
            },
            CancellationToken.None);

        Assert.NotNull(idSeenByFactory);
        Assert.True(idSeenByFactory > 0);
        Assert.Equal(quote.Id, idSeenByFactory);
    }
}
