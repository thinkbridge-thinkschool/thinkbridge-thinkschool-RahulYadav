using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;
using Xunit.Abstractions;

namespace QuotesApi.Tests;

// Day 21, Phase 9 — the core proof of stampede protection: many concurrent
// requests for the SAME cold cache key must not turn into that many
// database reads. Uses the real HybridCache implementation registered by
// Program.cs (via QuotesApiFactory) and a real EF Core/SQLite read (via
// GatedQuoteRepository, see GatedQuoteRepository.cs) — no fake cache, no
// homemade ConcurrentDictionary lock. Coalescing comes entirely from
// HybridCache.GetOrCreateAsync.
public sealed class HybridCacheStampedeTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private GatedQuotesApiFactory _factory = null!;
    private HttpClient _client = null!;

    public HybridCacheStampedeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync()
    {
        _factory = new GatedQuotesApiFactory();
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task HybridCache_ConcurrentColdRequests_CoalesceDatabaseLoad()
    {
        const int concurrency = 50;

        int quoteId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
            var creation = Quote.Create("Stampede Author", "Stampede protection quote.");
            db.Quotes.Add(creation.Quote!);
            await db.SaveChangesAsync();
            quoteId = creation.Quote!.Id;
        }

        // Cache is cold for this key: nothing has read it yet in this
        // freshly-created factory/service provider.
        var requestTasks = Enumerable.Range(0, concurrency)
            .Select(_ => _client.GetAsync($"/api/quotes/{quoteId}"))
            .ToArray();

        // Every one of the 50 requests either becomes the single leader
        // (blocked below on _factory.Gate) or a follower coalesced by
        // HybridCache behind that leader's in-flight task. The leader
        // literally cannot complete until Release() is called, so this
        // delay only needs to be long enough for all 50 in-memory
        // TestServer requests to be dispatched and reach that point — it
        // is not a "hope it finished in time" wait.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        _factory.Gate.Release();

        var responses = await Task.WhenAll(requestTasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var bodies = await Task.WhenAll(
            responses.Select(r => r.Content.ReadFromJsonAsync<QuoteDto>()));

        Assert.All(bodies, b => Assert.Equal(quoteId, b!.Id));
        Assert.All(bodies, b => Assert.Equal("Stampede Author", b!.Author));

        var diagnostics = await _client.GetFromJsonAsync<CacheDiagnosticsDto>(
            "/api/diagnostics/cache");

        // The core stampede assertion: exactly one caller ever entered the
        // repository/DB read for this cold key, even though 50 concurrent
        // requests asked for it.
        Assert.Equal(1, _factory.Gate.EntryCount);
        Assert.Equal(1, diagnostics!.FactoryExecutions);
        Assert.Equal(1, diagnostics.DatabaseQueries);
        Assert.Equal(concurrency, responses.Count(r => r.StatusCode == HttpStatusCode.OK));

        _output.WriteLine("=== HybridCache stampede protection evidence ===");
        _output.WriteLine($"Concurrent requests: {concurrency}");
        _output.WriteLine($"Cache key: quote:{quoteId}");
        _output.WriteLine($"Repository entries (should be 1): {_factory.Gate.EntryCount}");
        _output.WriteLine($"Factory executions: {diagnostics.FactoryExecutions}");
        _output.WriteLine($"DB quote queries: {diagnostics.DatabaseQueries}");
        _output.WriteLine($"Successful responses: {responses.Count(r => r.StatusCode == HttpStatusCode.OK)}");

        // Scope note: this proves single-flight coalescing within ONE
        // process/service provider (one HybridCache L1, and L2 only if
        // Redis is configured). It is not a claim of a distributed,
        // cross-instance exclusive lock — a second application instance
        // hitting a cold key at the same moment would independently run
        // its own single factory execution unless/until a shared Redis L2
        // already has the value.
    }

    private sealed record QuoteDto(int Id, string Author, string Text, bool IsDeleted);
}
