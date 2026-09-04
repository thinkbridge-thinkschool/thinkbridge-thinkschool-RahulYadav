using QuotesApi.Modules.Collections.Contracts.Events;
using QuotesApi.Modules.Notifications.Application.Ports;
using QuotesApi.Shared.Messaging;

namespace QuotesApi.Modules.Notifications.Application.EventHandlers;

internal sealed class QuoteRemovedFromCollectionNotificationHandler
    : IIntegrationEventHandler<QuoteRemovedFromCollection>
{
    private readonly INotificationStore _notificationStore;
    private readonly ILogger<QuoteRemovedFromCollectionNotificationHandler> _logger;

    public QuoteRemovedFromCollectionNotificationHandler(
        INotificationStore notificationStore,
        ILogger<QuoteRemovedFromCollectionNotificationHandler> logger)
    {
        _notificationStore = notificationStore;
        _logger = logger;
    }

    public async Task HandleAsync(QuoteRemovedFromCollection integrationEvent, CancellationToken cancellationToken)
    {
        var message =
            $"Quote {integrationEvent.QuoteId} was removed from collection {integrationEvent.CollectionId}.";

        await _notificationStore.RecordAsync(
            nameof(QuoteRemovedFromCollection),
            message,
            integrationEvent.RemovedAtUtc,
            cancellationToken);

        _logger.LogInformation(
            "Notification recorded for collection {CollectionId}: {Message}",
            integrationEvent.CollectionId,
            message);
    }
}
