namespace QuotesApi.Shared.Messaging;

public static class SharedMessagingExtensions
{
    public static IServiceCollection AddSharedMessaging(this IServiceCollection services)
    {
        services.AddScoped<IIntegrationEventPublisher, InProcessIntegrationEventPublisher>();

        return services;
    }
}
