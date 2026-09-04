using QuotesApi.Modules.Collections.Contracts.Events;
using QuotesApi.Modules.Notifications.Application.EventHandlers;
using QuotesApi.Modules.Notifications.Application.Ports;
using QuotesApi.Modules.Notifications.Infrastructure;
using QuotesApi.Shared.Messaging;

namespace QuotesApi.Modules.Notifications;

public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddScoped<INotificationStore, NotificationStore>();

        services.AddScoped<IIntegrationEventHandler<CollectionCreated>, CollectionCreatedNotificationHandler>();
        services.AddScoped<IIntegrationEventHandler<QuoteAddedToCollection>, QuoteAddedToCollectionNotificationHandler>();
        services.AddScoped<IIntegrationEventHandler<QuoteRemovedFromCollection>, QuoteRemovedFromCollectionNotificationHandler>();

        return services;
    }
}
