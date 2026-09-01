using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Tests;

// In-memory IQuoteRepository test double for exercising
// QuoteProcessingBackgroundService without a real database. Quote.Id has
// a private setter (EF Core assigns it via convention), so it's set here
// via reflection the same way EF Core would.
internal sealed class FakeQuoteRepository : IQuoteRepository
{
    private readonly Dictionary<int, Quote> _quotes = new();
    private int _nextId = 1;

    public List<int> GetByIdRequests { get; } = new();

    public HashSet<int> FailOnGetById { get; } = new();

    public Quote Seed(string author, string text)
    {
        var quote = Quote.Create(author, text).Quote!;
        SetId(quote, _nextId++);
        _quotes[quote.Id] = quote;
        return quote;
    }

    public Task<List<Quote>> GetQuotesAsync(
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_quotes.Values.ToList());
    }

    public Task<Quote?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        GetByIdRequests.Add(id);

        if (FailOnGetById.Contains(id))
            throw new InvalidOperationException($"Simulated repository failure for quote {id}.");

        _quotes.TryGetValue(id, out var quote);
        return Task.FromResult(quote);
    }

    public Task<Quote> AddAsync(Quote quote, CancellationToken cancellationToken)
    {
        SetId(quote, _nextId++);
        _quotes[quote.Id] = quote;
        return Task.FromResult(quote);
    }

    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        return Task.FromResult(_quotes.Remove(id));
    }

    private static void SetId(Quote quote, int id)
    {
        typeof(Quote).GetProperty(nameof(Quote.Id))!.SetValue(quote, id);
    }
}
