namespace QuotesApi.Caching;

// Day 21: lightweight, in-process cache metrics for the HybridCache-backed
// quote read path. Deliberately request-attributed rather than reading
// HybridCache's own internal telemetry: "hit" means this specific request
// got its answer WITHOUT itself running the factory (either the value was
// already cached, or the request was coalesced behind another in-flight
// factory execution by HybridCache's stampede protection); "miss" means
// this request's own call to GetOrCreateAsync is the one that executed the
// factory. That framing is exactly the thing this exercise cares about:
// hits are requests that did NOT cause database load.
public sealed class QuoteCacheMetrics
{
    private long _hits;
    private long _misses;
    private long _factoryExecutions;

    public void RecordHit() => Interlocked.Increment(ref _hits);

    public void RecordMiss() => Interlocked.Increment(ref _misses);

    public void RecordFactoryExecution() => Interlocked.Increment(ref _factoryExecutions);

    public long Hits => Interlocked.Read(ref _hits);

    public long Misses => Interlocked.Read(ref _misses);

    public long FactoryExecutions => Interlocked.Read(ref _factoryExecutions);

    public long TotalRequests => Hits + Misses;

    public double HitRate =>
        TotalRequests == 0
            ? 0d
            : Math.Round((double)Hits / TotalRequests, 4);

    public void Reset()
    {
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
        Interlocked.Exchange(ref _factoryExecutions, 0);
    }
}
