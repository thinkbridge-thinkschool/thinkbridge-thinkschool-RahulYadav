using QuotesApi.Modules.Collections.Contracts.Events;
using QuotesApi.Modules.Notifications.Application.Ports;
using QuotesApi.Shared.Messaging;

namespace QuotesApi.Modules.Notifications.Application.EventHandlers;

// Depends only on QuotesApi.Modules.Collections.Contracts.Events —
// Notifications never references Collections' Domain or Infrastructure, so
// it can react to "a collection was created" without ever touching
// Collections' persistence.
internal sealed class CollectionCreatedNotificationHandler : IIntegrationEventHandler<CollectionCreated>
{
    private readonly INotificationStore _notificationStore;
    private readonly ILogger<CollectionCreatedNotificationHandler> _logger;

    public CollectionCreatedNotificationHandler(
        INotificationStore notificationStore,
        ILogger<CollectionCreatedNotificationHandler> logger)
    {
        _notificationStore = notificationStore;
        _logger = logger;
    }

    public async Task HandleAsync(CollectionCreated integrationEvent, CancellationToken cancellationToken)
    {
        var message = $"Collection '{integrationEvent.Name}' was created.";

        await _notificationStore.RecordAsync(
            nameof(CollectionCreated),
            message,
            integrationEvent.CreatedAtUtc,
            cancellationToken);

        _logger.LogInformation(
            "Notification recorded for collection {CollectionId}: {Message}",
            integrationEvent.CollectionId,
            message);
    }
}
