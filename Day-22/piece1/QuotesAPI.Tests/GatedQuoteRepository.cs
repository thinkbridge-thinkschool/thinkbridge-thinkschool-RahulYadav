using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Tests;

// Day 21: deterministic concurrency primitive for the HybridCache stampede
// test (see HybridCacheTests.cs). Wraps the REAL QuoteRepository/SQLite
// path — the database read genuinely happens here, it is just held open by
// a TaskCompletionSource until the test explicitly releases it. This lets
// the test guarantee that all N concurrent HTTP requests have reached
// HybridCache.GetOrCreateAsync before the single leader's factory is
// allowed to complete, instead of relying on timing/sleep to (hopefully)
// create overlap.
internal sealed class GateSignal
{
    private readonly TaskCompletionSource _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _entryCount;

    public Task Gate => _tcs.Task;

    public int EntryCount => Volatile.Read(ref _entryCount);

    public void Enter() => Interlocked.Increment(ref _entryCount);

    public void Release() => _tcs.TrySetResult();
}

internal sealed class GatedQuoteRepository : IQuoteRepository
{
    private readonly QuoteRepository _inner;
    private readonly GateSignal _gate;

    public GatedQuoteRepository(QuotesDbContext context, GateSignal gate)
    {
        _inner = new QuoteRepository(context);
        _gate = gate;
    }

    public async Task<Quote?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        // Every concurrent caller that reaches this point is, by
        // definition, NOT coalesced by HybridCache — a passing stampede
        // test requires this to be entered exactly once.
        _gate.Enter();

        await _gate.Gate;

        return await _inner.GetByIdAsync(id, cancellationToken);
    }

    public Task<List<Quote>> GetQuotesAsync(int page, int size, CancellationToken cancellationToken) =>
        _inner.GetQuotesAsync(page, size, cancellationToken);

    public Task<Quote> AddAsync(
        Quote quote,
        Func<Quote, OutboxMessage> buildOutboxMessage,
        CancellationToken cancellationToken) =>
        _inner.AddAsync(quote, buildOutboxMessage, cancellationToken);

    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken) =>
        _inner.DeleteAsync(id, cancellationToken);
}
