using QuotesApi.Modules.Collections.Contracts.Events;
using QuotesApi.Modules.Notifications.Application.Ports;
using QuotesApi.Shared.Messaging;

namespace QuotesApi.Modules.Notifications.Application.EventHandlers;

internal sealed class QuoteAddedToCollectionNotificationHandler : IIntegrationEventHandler<QuoteAddedToCollection>
{
    private readonly INotificationStore _notificationStore;
    private readonly ILogger<QuoteAddedToCollectionNotificationHandler> _logger;

    public QuoteAddedToCollectionNotificationHandler(
        INotificationStore notificationStore,
        ILogger<QuoteAddedToCollectionNotificationHandler> logger)
    {
        _notificationStore = notificationStore;
        _logger = logger;
    }

    public async Task HandleAsync(QuoteAddedToCollection integrationEvent, CancellationToken cancellationToken)
    {
        var message =
            $"Quote {integrationEvent.QuoteId} was added to collection {integrationEvent.CollectionId}.";

        await _notificationStore.RecordAsync(
            nameof(QuoteAddedToCollection),
            message,
            integrationEvent.AddedAtUtc,
            cancellationToken);

        _logger.LogInformation(
            "Notification recorded for collection {CollectionId}: {Message}",
            integrationEvent.CollectionId,
            message);
    }
}
