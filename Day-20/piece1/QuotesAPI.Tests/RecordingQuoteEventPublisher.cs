using QuotesApi.Messaging;

namespace QuotesApi.Tests;

// Records every event actually handed to it, standing in for a real
// Service Bus publish so tests can assert "the message was actually
// published" without needing a live Azure Service Bus namespace. Always
// succeeds — publish failures are exercised separately via
// ThrowingQuoteEventPublisher.
internal sealed class RecordingQuoteEventPublisher : IQuoteEventPublisher
{
    private readonly object _gate = new();
    private readonly List<QuoteCreatedEvent> _publishedEvents = new();

    public IReadOnlyList<QuoteCreatedEvent> PublishedEvents
    {
        get { lock (_gate) { return _publishedEvents.ToList(); } }
    }

    public Task PublishQuoteCreatedAsync(
        QuoteCreatedEvent quoteCreated,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _publishedEvents.Add(quoteCreated);
        }

        return Task.CompletedTask;
    }
}
