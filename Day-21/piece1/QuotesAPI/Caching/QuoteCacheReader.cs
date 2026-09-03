using Microsoft.Extensions.Caching.Hybrid;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Caching;

// Day 21: fronts the hot GET /api/quotes/{id} read with Microsoft
// HybridCache (L1 in-process memory + L2 Redis when configured — see
// Extensions/CachingExtensions.cs). The factory below is EXACTLY where the
// database read happens: HybridCache.GetOrCreateAsync only invokes it on a
// real cache miss, and — this is the stampede protection this exercise is
// about — when many concurrent requests ask for the same cold key,
// GetOrCreateAsync coalesces them so the factory runs (at most) once per
// key per process while every caller still gets the resulting value. No
// homemade locking/ConcurrentDictionary is used; this is HybridCache's
// built-in single-flight behavior.
public sealed class QuoteCacheReader
{
    // No explicit HybridCacheEntryOptions is passed to GetOrCreateAsync
    // below — it uses HybridCacheOptions.DefaultEntryOptions, configured
    // once from QuoteCacheOptions in Extensions/CachingExtensions.cs. That
    // is also what lets appsettings.Testing.json set a short TTL for the
    // cache-expiration test without this class knowing about environments.
    private readonly HybridCache _cache;
    private readonly IQuoteRepository _repository;
    private readonly QuoteCacheMetrics _metrics;

    public QuoteCacheReader(
        HybridCache cache,
        IQuoteRepository repository,
        QuoteCacheMetrics metrics)
    {
        _cache = cache;
        _repository = repository;
        _metrics = metrics;
    }

    public async Task<CachedQuote?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var state = new FactoryState(id, _repository, _metrics);

        var cached = await _cache.GetOrCreateAsync(
            CacheKeys.Quote(id),
            state,
            static async (s, ct) =>
            {
                // Reached only by the single request HybridCache elects to
                // actually populate this key — every other concurrent
                // caller for the same cold key awaits this same call
                // instead of running its own.
                s.FactoryRan = true;
                s.Metrics.RecordFactoryExecution();

                var quote = await s.Repository.GetByIdAsync(s.Id, ct);

                return quote is null ? null : CachedQuote.FromQuote(quote);
            },
            cancellationToken: cancellationToken);

        if (state.FactoryRan)
        {
            _metrics.RecordMiss();
        }
        else
        {
            _metrics.RecordHit();
        }

        return cached;
    }

    public Task EvictAsync(int id, CancellationToken cancellationToken) =>
        _cache.RemoveAsync(CacheKeys.Quote(id), cancellationToken).AsTask();

    private sealed class FactoryState(int id, IQuoteRepository repository, QuoteCacheMetrics metrics)
    {
        public int Id { get; } = id;

        public IQuoteRepository Repository { get; } = repository;

        public QuoteCacheMetrics Metrics { get; } = metrics;

        public bool FactoryRan;
    }
}
