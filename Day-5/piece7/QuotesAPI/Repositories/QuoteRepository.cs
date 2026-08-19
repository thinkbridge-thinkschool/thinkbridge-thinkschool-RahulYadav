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

    public async Task<Quote> AddAsync(
        Quote quote,
        CancellationToken cancellationToken)
    {
        _context.Quotes.Add(quote);

        await _context.SaveChangesAsync(cancellationToken);

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