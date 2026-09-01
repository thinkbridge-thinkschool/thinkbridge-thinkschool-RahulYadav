using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuotesApi.Options;

namespace QuotesApi.Messaging;

// Publishes QuoteCreatedEvent to the Day 19 Service Bus topic. Registered
// as a singleton (see Program.cs) so the ServiceBusSender it wraps is
// created once and reused for the lifetime of the app, as the SDK
// recommends, rather than opened per message.
//
// Authentication is entirely via the ServiceBusClient injected from DI,
// which Program.cs constructs with an Azure identity credential (see
// Program.cs for the Development-vs-production choice) — no connection
// string or key ever passes through this class.
public sealed class ServiceBusQuoteEventPublisher : IQuoteEventPublisher, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;
    private readonly ILogger<ServiceBusQuoteEventPublisher> _logger;

    public ServiceBusQuoteEventPublisher(
        ServiceBusClient client,
        IOptions<ServiceBusOptions> options,
        ILogger<ServiceBusQuoteEventPublisher> logger)
    {
        _sender = client.CreateSender(options.Value.TopicName);
        _logger = logger;
    }

    public async Task PublishQuoteCreatedAsync(
        QuoteCreatedEvent quoteCreated,
        CancellationToken cancellationToken)
    {
        var message = new ServiceBusMessage(
            JsonSerializer.SerializeToUtf8Bytes(quoteCreated))
        {
            // The stable per-quote id doubles as the idempotency key every
            // subscriber dedupes on when it receives its copy of this
            // message (see ServiceBusSubscriptionWorker).
            MessageId = quoteCreated.MessageId,
            ContentType = "application/json",
            Subject = "QuoteCreated",
        };

        message.ApplicationProperties["QuoteId"] = quoteCreated.QuoteId;
        message.ApplicationProperties["EventType"] = "QuoteCreated";

        await _sender.SendMessageAsync(message, cancellationToken);

        _logger.LogInformation(
            "Published QuoteCreated event {MessageId} for quote {QuoteId} to topic {Topic}.",
            quoteCreated.MessageId,
            quoteCreated.QuoteId,
            _sender.EntityPath);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
    }
}
