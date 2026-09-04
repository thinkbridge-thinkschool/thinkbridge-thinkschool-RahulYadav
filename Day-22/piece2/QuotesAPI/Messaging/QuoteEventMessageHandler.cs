using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuotesApi.Repositories;

namespace QuotesApi.Messaging;

// Processing logic shared by every ServiceBusSubscriptionWorker instance
// (Worker A1, Worker A2, Worker B1 — see Program.cs). Scoped, so each
// message gets its own DbContext, matching the same per-work-item scoping
// QuoteProcessingBackgroundService already uses for the Day 18 local
// queue.
//
// Deliberately does not touch the Service Bus message/receiver itself —
// it returns normally on success and throws on failure, leaving
// complete/abandon/dead-letter settlement to the caller, which owns the
// ServiceBusReceivedMessage.
public sealed class QuoteEventMessageHandler
{
    private readonly IProcessedMessageStore _processedMessages;
    private readonly ILogger<QuoteEventMessageHandler> _logger;

    public QuoteEventMessageHandler(
        IProcessedMessageStore processedMessages,
        ILogger<QuoteEventMessageHandler> logger)
    {
        _processedMessages = processedMessages;
        _logger = logger;
    }

    public async Task HandleAsync(
        string subscriptionName,
        string workerName,
        string messageId,
        BinaryData body,
        CancellationToken cancellationToken)
    {
        // ------------------------------------------------------------
        // Idempotency check: the MessageId is the dedup key. A message
        // already recorded for this subscription is a duplicate delivery
        // (Service Bus is at-least-once) — skip it safely instead of
        // reprocessing.
        // ------------------------------------------------------------

        if (await _processedMessages.HasBeenProcessedAsync(
                subscriptionName, messageId, cancellationToken))
        {
            _logger.LogInformation(
                "[{Worker}/{Subscription}] {MessageId} already processed; skipping duplicate delivery.",
                workerName,
                subscriptionName,
                messageId);

            return;
        }

        var quoteCreated = JsonSerializer.Deserialize<QuoteCreatedEvent>(body.ToString());

        if (quoteCreated is null)
        {
            throw new InvalidOperationException(
                $"Message {messageId} could not be deserialized as {nameof(QuoteCreatedEvent)}.");
        }

        // ------------------------------------------------------------
        // Deliberate poison message: always throws, for every subscriber,
        // every attempt. Demonstrates the SDK's normal retry-then-dead-
        // letter behavior — the subscription's MaxDeliveryCount (3) is
        // reached and Service Bus moves the message to the real DLQ
        // itself; nothing here fakes that.
        // ------------------------------------------------------------

        if (quoteCreated.Author == QuoteCreatedEvent.PoisonAuthorMarker)
        {
            throw new InvalidOperationException(
                $"Simulated poison message {messageId} on {subscriptionName} " +
                "(Day 19 DLQ demo) — this always fails.");
        }

        _logger.LogInformation(
            "[{Worker}/{Subscription}] Processing quote {QuoteId} by {Author} ({MessageId}).",
            workerName,
            subscriptionName,
            quoteCreated.QuoteId,
            quoteCreated.Author,
            messageId);

        // Stand-in for real per-subscription work (e.g. subscription A
        // could feed a search index, subscription B could send a
        // notification). This exercise is about the messaging plumbing,
        // not that downstream work, so it's just a log line.

        await _processedMessages.MarkProcessedAsync(
            subscriptionName, messageId, cancellationToken);
    }
}
