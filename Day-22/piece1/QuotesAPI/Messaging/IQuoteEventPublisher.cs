namespace QuotesApi.Messaging;

// Publishes domain events for the Day 19 topic/subscription demo.
// Two implementations are registered depending on configuration (see
// Program.cs): ServiceBusQuoteEventPublisher when ServiceBus is
// configured, NullQuoteEventPublisher otherwise (tests, or an environment
// without Azure connectivity) — the HTTP endpoint that calls this never
// needs to know which one it got.
public interface IQuoteEventPublisher
{
    Task PublishQuoteCreatedAsync(
        QuoteCreatedEvent quoteCreated,
        CancellationToken cancellationToken);
}
