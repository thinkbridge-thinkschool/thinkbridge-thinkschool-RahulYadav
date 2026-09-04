using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Modules.Collections.Contracts.Dtos;
using QuotesApi.Modules.Notifications.Contracts;

namespace QuotesApi.Tests.Notifications;

// Proves the three async flows end-to-end: Collections publishes an
// integration event through the shared in-process bus, and Notifications
// (a module Collections has never heard of) reacts by recording a
// notification — all without Collections and Notifications sharing any
// persistence or entity types.
public sealed class NotificationFlowTests : IAsyncLifetime
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

    private async Task<int> SeedQuoteAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        var creation = Quote.Create("Seneca", "Luck is what happens when preparation meets opportunity.");
        db.Quotes.Add(creation.Quote!);
        await db.SaveChangesAsync();

        return creation.Quote!.Id;
    }

    private async Task<IReadOnlyList<NotificationDto>> GetNotificationsAsync() =>
        (await _client.GetFromJsonAsync<List<NotificationDto>>("/api/notifications"))!;

    [Fact]
    public async Task CreatingACollection_RecordsACollectionCreatedNotification()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/collections",
            new { Name = "Motivation", OwnerId = 1 });
        var collection = await response.Content.ReadFromJsonAsync<CollectionDto>();

        var notifications = await GetNotificationsAsync();

        Assert.Contains(
            notifications,
            n => n.EventType == "CollectionCreated" && n.Message.Contains(collection!.Name));
    }

    [Fact]
    public async Task AddingAQuote_RecordsAQuoteAddedToCollectionNotification()
    {
        var quoteId = await SeedQuoteAsync();

        var createResponse = await _client.PostAsJsonAsync(
            "/api/collections",
            new { Name = "Motivation", OwnerId = 1 });
        var collection = await createResponse.Content.ReadFromJsonAsync<CollectionDto>();

        await _client.PostAsJsonAsync($"/api/collections/{collection!.Id}/items", new { QuoteId = quoteId });

        var notifications = await GetNotificationsAsync();

        Assert.Contains(
            notifications,
            n => n.EventType == "QuoteAddedToCollection"
                 && n.Message.Contains($"Quote {quoteId}")
                 && n.Message.Contains($"collection {collection.Id}"));
    }

    [Fact]
    public async Task RemovingAQuote_RecordsAQuoteRemovedFromCollectionNotification()
    {
        var quoteId = await SeedQuoteAsync();

        var createResponse = await _client.PostAsJsonAsync(
            "/api/collections",
            new { Name = "Motivation", OwnerId = 1 });
        var collection = await createResponse.Content.ReadFromJsonAsync<CollectionDto>();

        await _client.PostAsJsonAsync($"/api/collections/{collection!.Id}/items", new { QuoteId = quoteId });
        await _client.DeleteAsync($"/api/collections/{collection.Id}/items/{quoteId}");

        var notifications = await GetNotificationsAsync();

        Assert.Contains(
            notifications,
            n => n.EventType == "QuoteRemovedFromCollection"
                 && n.Message.Contains($"Quote {quoteId}")
                 && n.Message.Contains($"collection {collection.Id}"));
    }
}
