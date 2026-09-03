using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

// EF Core-backed idempotency store. The (SubscriptionName, MessageId)
// primary key (see QuotesDbContext.OnModelCreating) is the actual
// correctness guarantee: MarkProcessedAsync tolerates the unique-constraint
// violation from a lost race between competing consumers instead of
// throwing, so two workers that both see "not processed yet" for the same
// message can't both record it twice or crash on the second insert.
public sealed class ProcessedMessageStore : IProcessedMessageStore
{
    private readonly QuotesDbContext _db;

    public ProcessedMessageStore(QuotesDbContext db)
    {
        _db = db;
    }

    public Task<bool> HasBeenProcessedAsync(
        string subscriptionName,
        string messageId,
        CancellationToken cancellationToken)
    {
        return _db.ProcessedMessages.AnyAsync(
            x => x.SubscriptionName == subscriptionName && x.MessageId == messageId,
            cancellationToken);
    }

    public async Task MarkProcessedAsync(
        string subscriptionName,
        string messageId,
        CancellationToken cancellationToken)
    {
        _db.ProcessedMessages.Add(new ProcessedMessage
        {
            SubscriptionName = subscriptionName,
            MessageId = messageId,
            ProcessedAtUtc = DateTimeOffset.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another competing consumer already recorded this message for
            // this subscription first. The work was still only "won" by
            // one of them from the broker's point of view for this
            // delivery; this just means our own bookkeeping lost the race,
            // which is safe to ignore.
            _db.Entry(_db.ProcessedMessages.Local
                    .First(x => x.SubscriptionName == subscriptionName && x.MessageId == messageId))
                .State = EntityState.Detached;
        }
    }
}
