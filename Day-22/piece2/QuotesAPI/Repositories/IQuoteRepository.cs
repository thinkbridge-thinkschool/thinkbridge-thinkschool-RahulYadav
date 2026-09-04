using QuotesApi.Models;

namespace QuotesApi.Repositories;

public interface IQuoteRepository
{
    Task<List<Quote>> GetQuotesAsync(
        int page,
        int size,
        CancellationToken cancellationToken);

    Task<Quote?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    // Day 20: writes the quote AND the outbox row describing it in the
    // SAME EF Core transaction, so the two can never diverge — either both
    // are durable or neither is. buildOutboxMessage receives the quote
    // AFTER it has an Id (the quote is saved first, inside the same
    // transaction, precisely so the outbox payload can reference it) and
    // must return the OutboxMessage row to insert alongside it. See
    // QuoteRepository.AddAsync for the transaction itself.
    Task<Quote> AddAsync(
        Quote quote,
        Func<Quote, OutboxMessage> buildOutboxMessage,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken);
}