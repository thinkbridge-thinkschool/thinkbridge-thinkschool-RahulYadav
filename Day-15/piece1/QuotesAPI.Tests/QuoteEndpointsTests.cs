using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Tests;

// Characterization tests for the real Week-1 QuotesApi contract, written
// BEFORE the Angular HttpClient/interceptor work so the frontend can be
// built against a proven contract instead of an assumed one.
//
// Key finding from exercising the real pipeline: this API does NOT use
// ASP.NET Core ProblemDetails/ValidationProblemDetails anywhere. Every 4xx
// from the quotes endpoints is `Results.BadRequest(<plain string>)`
// (see QuoteEndpointExtensions.cs), which serializes as a bare JSON string,
// not a ProblemDetails object. These tests assert that real, current shape.
// A fresh QuotesApiFactory (and therefore a fresh, empty SQLite file) is
// created per test rather than shared via IClassFixture, so seeded quotes
// and pagination assertions in one test can never collide with another.
public sealed class QuoteEndpointsTests : IAsyncLifetime
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

    private async Task SeedQuotesAsync(params (string Author, string Text)[] quotes)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        foreach (var (author, text) in quotes)
        {
            var creation = Quote.Create(author, text);
            db.Quotes.Add(creation.Quote!);
        }

        await db.SaveChangesAsync();
    }

    // ----------------------------------------------------------------
    // GET /api/quotes?page=&size= — success contract
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetQuotes_ReturnsOk_WithRealFieldsAndPaginationApplied()
    {
        await SeedQuotesAsync(
            ("Ada Lovelace", "The Analytical Engine weaves algebraic patterns."),
            ("Grace Hopper", "It's easier to ask forgiveness than permission."),
            ("Alan Turing", "Machines take me by surprise with great frequency."));

        var response = await _client.GetAsync("/api/quotes?page=1&size=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("application/json", response.Content.Headers.ContentType?.MediaType);

        var page1 = await response.Content.ReadFromJsonAsync<List<QuoteDto>>();
        Assert.NotNull(page1);
        Assert.Equal(2, page1!.Count);

        // Real fields from the response, not invented ones.
        Assert.All(page1, q =>
        {
            Assert.True(q.Id > 0);
            Assert.False(string.IsNullOrWhiteSpace(q.Author));
            Assert.False(string.IsNullOrWhiteSpace(q.Text));
        });
        Assert.Equal("Ada Lovelace", page1[0].Author);
        Assert.Equal("Grace Hopper", page1[1].Author);

        // Pagination parameters are actually applied (page 2 returns the remainder).
        var page2Response = await _client.GetAsync("/api/quotes?page=2&size=2");
        var page2 = await page2Response.Content.ReadFromJsonAsync<List<QuoteDto>>();
        Assert.Single(page2!);
        Assert.Equal("Alan Turing", page2![0].Author);
    }

    [Fact]
    public async Task GetQuotes_DefaultsPageAndSize_WhenQueryParamsOmitted()
    {
        await SeedQuotesAsync(("Default Page Author", "Default page quote text."));

        // No ?page=&size= at all — endpoint defaults to page=1, size=10.
        var response = await _client.GetAsync("/api/quotes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quotes = await response.Content.ReadFromJsonAsync<List<QuoteDto>>();
        Assert.Contains(quotes!, q => q.Author == "Default Page Author");
    }

    // ----------------------------------------------------------------
    // GET /api/quotes?page=&size= — real 4xx validation path
    // ----------------------------------------------------------------

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 0)]
    [InlineData(-1, 5)]
    public async Task GetQuotes_InvalidPagination_ReturnsPlainStringBadRequest(int page, int size)
    {
        var response = await _client.GetAsync($"/api/quotes?page={page}&size={size}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var jsonDocument = JsonDocument.Parse(body);

        // The real shape is a bare JSON string, NOT a ProblemDetails/
        // ValidationProblemDetails object. Asserting the JSON kind here
        // documents that deviation from the brief's ProblemDetails assumption.
        Assert.Equal(JsonValueKind.String, jsonDocument.RootElement.ValueKind);
        Assert.Equal("Page and size must be greater than 0.", jsonDocument.RootElement.GetString());
    }

    // ----------------------------------------------------------------
    // GET /api/quotes/{id} — not found
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetQuoteById_UnknownId_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/quotes/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ----------------------------------------------------------------
    // POST /api/quotes — requires authentication
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateQuote_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/quotes",
            new { author = "Someone", text = "Some quote" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ----------------------------------------------------------------
    // POST /api/quotes — real domain validation 4xx path (authenticated)
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateQuote_EmptyAuthor_ReturnsPlainStringBadRequest()
    {
        var token = await LoginAsSeededUserAsync();
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync(
            "/api/quotes",
            new { author = "", text = "A quote with no author" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var jsonDocument = JsonDocument.Parse(body);

        Assert.Equal(JsonValueKind.String, jsonDocument.RootElement.ValueKind);
        Assert.Equal("Author is required.", jsonDocument.RootElement.GetString());
    }

    [Fact]
    public async Task CreateQuote_ValidRequest_ReturnsCreatedWithRealFields()
    {
        var token = await LoginAsSeededUserAsync();
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync(
            "/api/quotes",
            new { author = "Margaret Hamilton", text = "Software engineering, before the term existed." });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<QuoteDto>();
        Assert.NotNull(created);
        Assert.True(created!.Id > 0);
        Assert.Equal("Margaret Hamilton", created.Author);
        Assert.Equal("Software engineering, before the term existed.", created.Text);
    }

    private async Task<string> LoginAsSeededUserAsync()
    {
        // Program.cs seeds this user on startup when the Users table is empty.
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "test@example.com", password = "Password123!" });

        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        return login!.AccessToken;
    }

    // Local DTOs mirroring the real JSON shape returned by the API — not new
    // API surface, just a typed view for deserialization in tests (same
    // approach the Angular app takes with its own Quote interface).
    private sealed record QuoteDto(int Id, string Author, string Text, bool IsDeleted);

    private sealed record LoginResponseDto(string AccessToken, string RefreshToken, int ExpiresIn);
}
