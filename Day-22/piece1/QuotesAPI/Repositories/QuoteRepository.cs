using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public class QuoteRepository : IQuoteRepository
{
    private readonly QuotesDbContext _context;

    public QuoteRepository(QuotesDbContext context)
    {
        _context = context;
    }

    public async Task<List<Quote>> GetQuotesAsync(
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        return await _context.Quotes
            .AsNoTracking()
            .Where(q => !q.IsDeleted)
            .OrderBy(q => q.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
    }

    public async Task<Quote?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await _context.Quotes
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted, cancellationToken);
    }

    // Day 20: Transactional Outbox. The quote insert and the outbox insert
    // happen inside one explicit EF Core transaction — either both commit
    // or neither does. buildOutboxMessage runs AFTER the quote's Id is
    // assigned (SQLite autoincrement only assigns it on SaveChangesAsync)
    // but still BEFORE the transaction commits, so the outbox row it
    // returns is written atomically with the quote it describes rather
    // than as a separate, potentially-divergent step.
    public async Task<Quote> AddAsync(
        Quote quote,
        Func<Quote, OutboxMessage> buildOutboxMessage,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await _context.Database.BeginTransactionAsync(cancellationToken);

        _context.Quotes.Add(quote);

        // Assigns quote.Id without leaving the transaction — the row is
        // not visible to any other connection until Commit below.
        await _context.SaveChangesAsync(cancellationToken);

        var outboxMessage = buildOutboxMessage(quote);

        _context.OutboxMessages.Add(outboxMessage);

        await _context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return quote;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var quote = await _context.Quotes
            .FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted, cancellationToken);

        if (quote is null)
            return false;

        quote.SoftDelete();

        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}