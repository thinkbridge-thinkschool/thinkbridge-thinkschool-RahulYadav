using QuotesApi.Caching;
using QuotesApi.Data;
using QuotesApi.Repositories;

namespace QuotesApi.Extensions;

// Day 21: diagnostics for the HybridCache-fronted quote read path. No
// secrets or connection details are exposed here — only counters.
public static class DiagnosticsEndpointExtensions
{
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/diagnostics");

        group.MapGet("/cache", (
            QuoteCacheMetrics metrics,
            DbQueryCounter dbQueryCounter) =>
        {
            return Results.Ok(new
            {
                hits = metrics.Hits,
                misses = metrics.Misses,
                totalRequests = metrics.TotalRequests,
                hitRate = metrics.HitRate,
                factoryExecutions = metrics.FactoryExecutions,
                databaseQueries = dbQueryCounter.QuoteReadCommands,
                totalDatabaseCommands = dbQueryCounter.TotalCommands,
                totalDatabaseElapsedMs = dbQueryCounter.TotalElapsed.TotalMilliseconds
            });
        });

        // Load-test/benchmark support: resets the in-process counters (NOT
        // the cache contents) so repeated before/after runs each start from
        // a clean measurement window.
        group.MapPost("/cache/reset", (
            QuoteCacheMetrics metrics,
            DbQueryCounter dbQueryCounter) =>
        {
            metrics.Reset();
            dbQueryCounter.Reset();
            return Results.NoContent();
        });

        // Load-test/benchmark support: evicts one quote's cache entry so a
        // "cold cache" run can be reproduced on demand instead of waiting
        // out the TTL.
        group.MapPost("/cache/evict/{id:int}", async (
            int id,
            QuoteCacheReader cacheReader,
            CancellationToken cancellationToken) =>
        {
            await cacheReader.EvictAsync(id, cancellationToken);
            return Results.NoContent();
        });

        // Benchmark-only comparison path: reads the SAME repository/DbContext
        // path as the cached endpoint but always bypasses HybridCache. This
        // is what the Day 21 "before" load-test measurements hit — the real
        // public GET /api/quotes/{id} endpoint always goes through the
        // cache; this route exists purely so the before/after comparison
        // exercises identical DB/repository code with caching as the only
        // variable.
        group.MapGet("/quotes/{id:int}/uncached", async (
            int id,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var quote = await repository.GetByIdAsync(id, cancellationToken);

            return quote is null
                ? Results.NotFound()
                : Results.Ok(QuotesApi.Models.CachedQuote.FromQuote(quote));
        });

        return app;
    }
}
