using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;
using Xunit.Abstractions;

namespace QuotesApi.Tests;

// Day 21, Phases 7-8-10 — a real, executed load test comparing the hot
// GET /api/quotes/{id} path with caching disabled/bypassed (the
// diagnostics-only /api/diagnostics/quotes/{id}/uncached route, which reads
// through the exact same repository/DbContext code) against the real
// cached endpoint. Runs against the in-memory ASP.NET Core TestServer
// (QuotesApiFactory) — no real network — so absolute latencies are lower
// than a deployed service, but the comparison is apples-to-apples since
// both runs share the same process, same SQLite database, and same
// request/concurrency shape. All numbers below are measured by this test
// run, not invented.
public sealed class HybridCacheLoadTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private LoadTestQuotesApiFactory _factory = null!;
    private HttpClient _client = null!;

    public HybridCacheLoadTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync()
    {
        _factory = new LoadTestQuotesApiFactory();
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<int> SeedQuoteAsync(string author, string text)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        var creation = Quote.Create(author, text);
        db.Quotes.Add(creation.Quote!);
        await db.SaveChangesAsync();

        return creation.Quote!.Id;
    }

    private async Task ResetCountersAsync()
    {
        var response = await _client.PostAsync("/api/diagnostics/cache/reset", content: null);
        response.EnsureSuccessStatusCode();
    }

    private async Task EvictAsync(int quoteId)
    {
        var response = await _client.PostAsync($"/api/diagnostics/cache/evict/{quoteId}", content: null);
        response.EnsureSuccessStatusCode();
    }

    private async Task<BenchmarkResult> RunLoadAsync(
        string path,
        int concurrency,
        int totalRequests)
    {
        var latencies = new double[totalRequests];
        var successCount = 0;

        var stopwatch = Stopwatch.StartNew();

        // Bounded concurrency: `concurrency` requests in flight at once,
        // `totalRequests` issued overall — matches the brief's
        // "Concurrency: 50, Requests: 100-200" shape.
        using var throttle = new SemaphoreSlim(concurrency);

        var tasks = new Task[totalRequests];
        for (var i = 0; i < totalRequests; i++)
        {
            var index = i;
            tasks[index] = Task.Run(async () =>
            {
                await throttle.WaitAsync();
                try
                {
                    var requestStopwatch = Stopwatch.StartNew();
                    var response = await _client.GetAsync(path);
                    requestStopwatch.Stop();

                    latencies[index] = requestStopwatch.Elapsed.TotalMilliseconds;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        Interlocked.Increment(ref successCount);
                    }
                }
                finally
                {
                    throttle.Release();
                }
            });
        }

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        Array.Sort(latencies);
        var p99Index = (int)Math.Ceiling(0.99 * latencies.Length) - 1;
        p99Index = Math.Clamp(p99Index, 0, latencies.Length - 1);

        return new BenchmarkResult(
            TotalRequests: totalRequests,
            Concurrency: concurrency,
            SuccessfulRequests: successCount,
            TotalDurationMs: stopwatch.Elapsed.TotalMilliseconds,
            P99LatencyMs: latencies[p99Index]);
    }

    [Fact]
    public async Task LoadTest_BeforeVsAfter_MeasuresDbLoadAndLatency()
    {
        const int concurrency = 50;
        const int totalRequests = 200;

        var quoteId = await SeedQuoteAsync("Load Test Author", "Load test quote text.");

        // ------------------------------------------------------------
        // BEFORE: caching bypassed (uncached diagnostics route), same
        // repository/DbContext path, same quote id, same concurrency.
        // ------------------------------------------------------------

        await ResetCountersAsync();

        var before = await RunLoadAsync(
            $"/api/diagnostics/quotes/{quoteId}/uncached",
            concurrency,
            totalRequests);

        var beforeDiagnostics = await _client.GetFromJsonAsync<CacheDiagnosticsDto>(
            "/api/diagnostics/cache");

        // ------------------------------------------------------------
        // AFTER: real cached endpoint, cold start forced via eviction so
        // the run includes a genuine cache miss before it warms up.
        // ------------------------------------------------------------

        await ResetCountersAsync();
        await EvictAsync(quoteId);

        var after = await RunLoadAsync(
            $"/api/quotes/{quoteId}",
            concurrency,
            totalRequests);

        var afterDiagnostics = await _client.GetFromJsonAsync<CacheDiagnosticsDto>(
            "/api/diagnostics/cache");

        // ------------------------------------------------------------
        // Report — real measured numbers only.
        // ------------------------------------------------------------

        var beforeDbQueriesPerSec =
            before.TotalDurationMs > 0
                ? beforeDiagnostics!.DatabaseQueries / (before.TotalDurationMs / 1000.0)
                : 0;

        var afterDbQueriesPerSec =
            after.TotalDurationMs > 0
                ? afterDiagnostics!.DatabaseQueries / (after.TotalDurationMs / 1000.0)
                : 0;

        var dbLoadReductionPercent =
            beforeDiagnostics!.DatabaseQueries == 0
                ? 0
                : (1.0 - (double)afterDiagnostics!.DatabaseQueries / beforeDiagnostics.DatabaseQueries) * 100.0;

        _output.WriteLine("=== BEFORE (cache bypassed) ===");
        _output.WriteLine($"Requests: {before.TotalRequests}, Concurrency: {before.Concurrency}");
        _output.WriteLine($"Successful: {before.SuccessfulRequests}");
        _output.WriteLine($"DB queries: {beforeDiagnostics.DatabaseQueries}");
        _output.WriteLine($"Total duration: {before.TotalDurationMs:F1} ms");
        _output.WriteLine($"DB queries/sec: {beforeDbQueriesPerSec:F1}");
        _output.WriteLine($"p99 latency: {before.P99LatencyMs:F2} ms");

        _output.WriteLine(string.Empty);
        _output.WriteLine("=== AFTER (HybridCache) ===");
        _output.WriteLine($"Requests: {after.TotalRequests}, Concurrency: {after.Concurrency}");
        _output.WriteLine($"Successful: {after.SuccessfulRequests}");
        _output.WriteLine($"DB queries: {afterDiagnostics!.DatabaseQueries}");
        _output.WriteLine($"Cache hits: {afterDiagnostics.Hits}, misses: {afterDiagnostics.Misses}");
        _output.WriteLine($"Cache hit rate: {afterDiagnostics.HitRate:P2}");
        _output.WriteLine($"Total duration: {after.TotalDurationMs:F1} ms");
        _output.WriteLine($"DB queries/sec: {afterDbQueriesPerSec:F1}");
        _output.WriteLine($"p99 latency: {after.P99LatencyMs:F2} ms");

        _output.WriteLine(string.Empty);
        _output.WriteLine($"DB load reduction: {dbLoadReductionPercent:F2}%");

        // ------------------------------------------------------------
        // Assertions — the point of the exercise, not just a report.
        // ------------------------------------------------------------

        Assert.Equal(totalRequests, before.SuccessfulRequests);
        Assert.Equal(totalRequests, after.SuccessfulRequests);

        Assert.Equal(totalRequests, beforeDiagnostics.DatabaseQueries);
        Assert.True(
            afterDiagnostics.DatabaseQueries < totalRequests / 10,
            $"Expected far fewer than {totalRequests / 10} DB queries with caching, got {afterDiagnostics.DatabaseQueries}.");

        Assert.True(afterDiagnostics.HitRate > 0.9);
    }

    private sealed record BenchmarkResult(
        int TotalRequests,
        int Concurrency,
        int SuccessfulRequests,
        double TotalDurationMs,
        double P99LatencyMs);
}

// appsettings.Testing.json sets QuoteCache TTL to ~1s so the dedicated
// expiration test (HybridCacheTests.HybridCache_EntryExpires_...) can run
// fast without a 30s sleep. That TTL is too short for a 200-request/50-
// concurrency benchmark, which could legitimately take longer than 1s on a
// loaded CI box and would then measure TTL expirations, not caching. This
// factory overrides QuoteCache to a much longer TTL so the load test
// measures the caching effect in isolation.
internal sealed class LoadTestQuotesApiFactory : QuotesApiFactory
{
    protected override void ConfigureAdditionalConfiguration(IConfigurationBuilder config)
    {
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["QuoteCache:Expiration"] = "00:05:00",
            ["QuoteCache:LocalCacheExpiration"] = "00:05:00"
        });
    }
}
