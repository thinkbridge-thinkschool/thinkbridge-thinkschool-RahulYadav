namespace QuotesApi.Shared.Messaging;

// In-process pub/sub: handlers are resolved from DI by TEvent, so a
// publishing module never references the modules that consume its events —
// only their shared Contracts event types. This is the "simple in-process
// event dispatcher" the modular-monolith design calls for; it runs the
// handlers synchronously within the same request right after the publishing
// module's own persistence, which keeps the demo deterministic (no polling,
// no background races) while still keeping the modules decoupled.
//
// Durability for cross-process/cross-deployment delivery is a separate
// concern already solved by the Day 20 Transactional Outbox
// (Messaging/OutboxRelayBackgroundService) — if a module boundary here were
// ever extracted into its own service, publishing through the outbox instead
// of this dispatcher is the natural next step, and nothing in the module's
// own code would need to change since it only depends on
// IIntegrationEventPublisher.
public sealed class InProcessIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InProcessIntegrationEventPublisher> _logger;

    public InProcessIntegrationEventPublisher(
        IServiceProvider serviceProvider,
        ILogger<InProcessIntegrationEventPublisher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        var handlers = _serviceProvider.GetServices<IIntegrationEventHandler<TEvent>>();

        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync(integrationEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                // One handler failing (e.g. Notifications) must never break
                // the publishing module's own request/transaction.
                _logger.LogError(
                    ex,
                    "Integration event handler {Handler} failed while handling {Event}",
                    handler.GetType().Name,
                    typeof(TEvent).Name);
            }
        }
    }
}
