using Microsoft.Extensions.Logging;

namespace QuotesApi.Messaging;

// No-op stand-in for IQuoteEventPublisher used whenever
// ServiceBus:FullyQualifiedNamespace is not configured — the Testing
// environment (appsettings.Testing.json has no ServiceBus section) and any
// local run without Azure connectivity. Keeps POST /api/quotes working
// without a live Service Bus namespace, the same way Program.cs already
// makes Azure Key Vault and Azure Monitor optional.
public sealed class NullQuoteEventPublisher : IQuoteEventPublisher
{
    private readonly ILogger<NullQuoteEventPublisher> _logger;

    public NullQuoteEventPublisher(ILogger<NullQuoteEventPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishQuoteCreatedAsync(
        QuoteCreatedEvent quoteCreated,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Service Bus is not configured; skipping publish of {MessageId}.",
            quoteCreated.MessageId);

        return Task.CompletedTask;
    }
}
