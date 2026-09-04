using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Tests;

// Day 21: behavior tests for the HybridCache-fronted GET /api/quotes/{id}
// read path (see Caching/QuoteCacheReader.cs). These exercise the REAL
// HybridCache implementation, the real EF Core/SQLite repository, and the
// real DbQueryCounterInterceptor through the full ASP.NET Core pipeline
// (QuotesApiFactory) — nothing here mocks HybridCache or the mechanism
// being demonstrated.
public sealed class HybridCacheTests : IAsyncLifetime
{
    private QuotesApiFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new QuotesApiFactory();
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

    private Task<CacheDiagnosticsDto> GetDiagnosticsAsync() =>
        _client.GetFromJsonAsync<CacheDiagnosticsDto>("/api/diagnostics/cache")!;

    // ----------------------------------------------------------------
    // Cache miss -> real DB read
    // ----------------------------------------------------------------

    [Fact]
    public async Task HybridCache_ColdRequest_ReadsDatabaseExactlyOnce()
    {
        var quoteId = await SeedQuoteAsync("Cold Author", "Cold cache quote text.");

        var response = await _client.GetAsync($"/api/quotes/{quoteId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var quote = await response.Content.ReadFromJsonAsync<QuoteDto>();
        Assert.Equal(quoteId, quote!.Id);
        Assert.Equal("Cold Author", quote.Author);

        var diagnostics = await GetDiagnosticsAsync();
        Assert.Equal(1, diagnostics.FactoryExecutions);
        Assert.Equal(1, diagnostics.DatabaseQueries);
        Assert.Equal(1, diagnostics.Misses);
        Assert.Equal(0, diagnostics.Hits);
    }

    // ----------------------------------------------------------------
    // Cache hit -> no additional DB read
    // ----------------------------------------------------------------

    [Fact]
    public async Task HybridCache_WarmRequest_ServedFromCacheWithoutAdditionalDatabaseQuery()
    {
        var quoteId = await SeedQuoteAsync("Warm Author", "Warm cache quote text.");

        var first = await _client.GetAsync($"/api/quotes/{quoteId}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var afterFirst = await GetDiagnosticsAsync();
        Assert.Equal(1, afterFirst.DatabaseQueries);

        var second = await _client.GetAsync($"/api/quotes/{quoteId}");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var secondQuote = await second.Content.ReadFromJsonAsync<QuoteDto>();
        Assert.Equal(quoteId, secondQuote!.Id);
        Assert.Equal("Warm Author", secondQuote.Author);

        var afterSecond = await GetDiagnosticsAsync();

        // The defining assertion: a second request for the same key does
        // NOT cause a second database read.
        Assert.Equal(1, afterSecond.DatabaseQueries);
        Assert.Equal(1, afterSecond.FactoryExecutions);
        Assert.Equal(1, afterSecond.Misses);
        Assert.Equal(1, afterSecond.Hits);
    }

    // ----------------------------------------------------------------
    // Cache metrics / hit rate
    // ----------------------------------------------------------------

    [Fact]
    public async Task CacheDiagnostics_ReportsAccurateHitMissAndHitRate()
    {
        var quoteId = await SeedQuoteAsync("Metrics Author", "Metrics quote text.");

        await _client.GetAsync($"/api/quotes/{quoteId}"); // miss
        await _client.GetAsync($"/api/quotes/{quoteId}"); // hit
        await _client.GetAsync($"/api/quotes/{quoteId}"); // hit

        var diagnostics = await GetDiagnosticsAsync();

        Assert.Equal(1, diagnostics.Misses);
        Assert.Equal(2, diagnostics.Hits);
        Assert.Equal(3, diagnostics.TotalRequests);
        Assert.Equal(1, diagnostics.FactoryExecutions);
        Assert.Equal(1, diagnostics.DatabaseQueries);
        Assert.Equal(2.0 / 3.0, diagnostics.HitRate, precision: 3);
    }

    // ----------------------------------------------------------------
    // Cache expiration -> refresh
    // ----------------------------------------------------------------

    [Fact]
    public async Task HybridCache_EntryExpires_CausesRefreshOnNextRequest()
    {
        // appsettings.Testing.json configures a 1-second QuoteCache TTL
        // specifically so this test can observe a real expiration without
        // waiting out the 30-second production default.
        var quoteId = await SeedQuoteAsync("Expiry Author", "Expiry quote text.");

        await _client.GetAsync($"/api/quotes/{quoteId}");
        var afterFirst = await GetDiagnosticsAsync();
        Assert.Equal(1, afterFirst.DatabaseQueries);

        await _client.GetAsync($"/api/quotes/{quoteId}");
        var afterSecond = await GetDiagnosticsAsync();
        Assert.Equal(1, afterSecond.DatabaseQueries); // still within TTL

        await Task.Delay(TimeSpan.FromMilliseconds(1500));

        var third = await _client.GetAsync($"/api/quotes/{quoteId}");
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);

        var afterThird = await GetDiagnosticsAsync();
        Assert.Equal(2, afterThird.DatabaseQueries); // expired entry refreshed
    }

    // ----------------------------------------------------------------
    // Delete invalidates the cache entry
    // ----------------------------------------------------------------

    [Fact]
    public async Task DeleteQuote_EvictsCacheEntry_SubsequentGetIsNotFound()
    {
        var quoteId = await SeedQuoteAsync("Delete Author", "Delete quote text.");

        // Warm the cache.
        var warm = await _client.GetAsync($"/api/quotes/{quoteId}");
        Assert.Equal(HttpStatusCode.OK, warm.StatusCode);

        var token = await LoginAsSeededUserAsync();
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var delete = await _client.DeleteAsync($"/api/quotes/{quoteId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        // Without the eviction wired into the DELETE endpoint, this would
        // still return the pre-delete cached value until the TTL expired.
        var afterDelete = await _client.GetAsync($"/api/quotes/{quoteId}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    private async Task<string> LoginAsSeededUserAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "test@example.com", password = "Password123!" });

        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        return login!.AccessToken;
    }

    private sealed record QuoteDto(int Id, string Author, string Text, bool IsDeleted);

    private sealed record LoginResponseDto(string AccessToken, string RefreshToken, int ExpiresIn);
}

// Shared response DTO for GET /api/diagnostics/cache, used by every Day 21
// cache test file in this project.
public sealed record CacheDiagnosticsDto(
    long Hits,
    long Misses,
    long TotalRequests,
    double HitRate,
    long FactoryExecutions,
    long DatabaseQueries,
    long TotalDatabaseCommands,
    double TotalDatabaseElapsedMs);
