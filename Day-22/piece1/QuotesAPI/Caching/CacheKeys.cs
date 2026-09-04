namespace QuotesApi.Caching;

public static class CacheKeys
{
    // Deterministic, per-entity key. Stable across process restarts and
    // across app instances sharing the same Redis L2, which is what lets
    // GetOrCreateAsync coalesce concurrent requests for the same quote.
    public static string Quote(int id) => $"quote:{id}";
}
