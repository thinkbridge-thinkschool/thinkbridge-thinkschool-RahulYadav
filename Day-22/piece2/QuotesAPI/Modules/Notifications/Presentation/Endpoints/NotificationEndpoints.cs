using QuotesApi.Modules.Notifications.Application.Ports;

namespace QuotesApi.Modules.Notifications.Presentation.Endpoints;

// Not part of the folder shape suggested for Notifications, but a thin
// read-only endpoint here is the simplest way to make the asynchronous
// CollectionCreated / QuoteAddedToCollection / QuoteRemovedFromCollection
// flows independently observable over HTTP.
public static class NotificationEndpoints
{
    public static void MapNotificationsModuleEndpoints(this WebApplication app)
    {
        app.MapGet("/api/notifications", GetRecentAsync);
    }

    private static async Task<IResult> GetRecentAsync(
        INotificationStore notificationStore,
        CancellationToken cancellationToken)
    {
        var notifications = await notificationStore.GetRecentAsync(50, cancellationToken);
        return Results.Ok(notifications);
    }
}
