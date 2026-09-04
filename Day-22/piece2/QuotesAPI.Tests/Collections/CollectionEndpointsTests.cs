using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Modules.Collections.Contracts.Dtos;

namespace QuotesApi.Tests.Collections;

// End-to-end tests through the real ASP.NET Core pipeline (Program.cs, DI,
// SQLite), proving the modular wiring — endpoint -> command handler ->
// EfCollectionRepository -> QuotesDbContext -> Collections/CollectionItems
// tables — actually works, not just that the aggregate's own rules do.
public sealed class CollectionEndpointsTests : IAsyncLifetime
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

    private async Task<int> SeedQuoteAsync(string author = "Marcus Aurelius", string text = "You have power over your mind.")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        var creation = Quote.Create(author, text);
        db.Quotes.Add(creation.Quote!);
        await db.SaveChangesAsync();

        return creation.Quote!.Id;
    }

    [Fact]
    public async Task CreateCollection_WithValidRequest_ReturnsCreated()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/collections",
            new { Name = "Motivation", OwnerId = 1 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<CollectionDto>();
        Assert.NotNull(dto);
        Assert.Equal("Motivation", dto!.Name);
        Assert.Equal(1, dto.OwnerId);
        Assert.Empty(dto.Quotes);
    }

    [Fact]
    public async Task CreateCollection_WithEmptyName_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/collections",
            new { Name = "", OwnerId = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetCollection_UnknownId_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/collections/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddQuote_ToExistingCollection_ReturnsOkWithMembership()
    {
        var quoteId = await SeedQuoteAsync();

        var createResponse = await _client.PostAsJsonAsync(
            "/api/collections",
            new { Name = "Motivation", OwnerId = 1 });
        var collection = await createResponse.Content.ReadFromJsonAsync<CollectionDto>();

        var addResponse = await _client.PostAsJsonAsync(
            $"/api/collections/{collection!.Id}/items",
            new { QuoteId = quoteId });

        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        var updated = await addResponse.Content.ReadFromJsonAsync<CollectionDto>();
        var membership = Assert.Single(updated!.Quotes);
        Assert.Equal(quoteId, membership.QuoteId);
    }

    [Fact]
    public async Task AddQuote_UnknownQuote_ReturnsBadRequest()
    {
        var createResponse = await _client.PostAsJsonAsync(
            "/api/collections",
            new { Name = "Motivation", OwnerId = 1 });
        var collection = await createResponse.Content.ReadFromJsonAsync<CollectionDto>();

        var addResponse = await _client.PostAsJsonAsync(
            $"/api/collections/{collection!.Id}/items",
            new { QuoteId = 999999 });

        Assert.Equal(HttpStatusCode.BadRequest, addResponse.StatusCode);
    }

    [Fact]
    public async Task AddQuote_Duplicate_ReturnsBadRequest()
    {
        var quoteId = await SeedQuoteAsync();

        var createResponse = await _client.PostAsJsonAsync(
            "/api/collections",
            new { Name = "Motivation", OwnerId = 1 });
        var collection = await createResponse.Content.ReadFromJsonAsync<CollectionDto>();

        await _client.PostAsJsonAsync($"/api/collections/{collection!.Id}/items", new { QuoteId = quoteId });
        var secondAdd = await _client.PostAsJsonAsync($"/api/collections/{collection.Id}/items", new { QuoteId = quoteId });

        Assert.Equal(HttpStatusCode.BadRequest, secondAdd.StatusCode);
    }

    [Fact]
    public async Task AddQuote_ToUnknownCollection_ReturnsNotFound()
    {
        var quoteId = await SeedQuoteAsync();

        var response = await _client.PostAsJsonAsync(
            "/api/collections/999/items",
            new { QuoteId = quoteId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemoveQuote_ExistingMembership_ReturnsNoContent()
    {
        var quoteId = await SeedQuoteAsync();

        var createResponse = await _client.PostAsJsonAsync(
            "/api/collections",
            new { Name = "Motivation", OwnerId = 1 });
        var collection = await createResponse.Content.ReadFromJsonAsync<CollectionDto>();

        await _client.PostAsJsonAsync($"/api/collections/{collection!.Id}/items", new { QuoteId = quoteId });

        var removeResponse = await _client.DeleteAsync($"/api/collections/{collection.Id}/items/{quoteId}");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        var getResponse = await _client.GetAsync($"/api/collections/{collection.Id}");
        var afterRemoval = await getResponse.Content.ReadFromJsonAsync<CollectionDto>();
        Assert.Empty(afterRemoval!.Quotes);
    }

    [Fact]
    public async Task RemoveQuote_NotInCollection_ReturnsBadRequest()
    {
        var createResponse = await _client.PostAsJsonAsync(
            "/api/collections",
            new { Name = "Motivation", OwnerId = 1 });
        var collection = await createResponse.Content.ReadFromJsonAsync<CollectionDto>();

        var response = await _client.DeleteAsync($"/api/collections/{collection!.Id}/items/12");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
