namespace QuotesApi.Shared.Messaging;

// The only door between modules for asynchronous, decoupled reactions. A
// publishing module (e.g. Collections) depends on this interface only — it
// never knows which modules, if any, are listening.
public interface IIntegrationEventPublisher
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent;
}
