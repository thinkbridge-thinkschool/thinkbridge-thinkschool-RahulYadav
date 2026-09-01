namespace QuotesApi.Messaging;

// Integration event published to the Day 19 Service Bus topic
// (ServiceBusOptions.TopicName) whenever a quote is created through the
// existing POST /api/quotes endpoint. This is deliberately a thin event —
// it exists to demonstrate topic/subscription fan-out, competing
// consumers, idempotency, and dead-lettering, not to model a rich domain.
//
// MessageId doubles as the Service Bus MessageId (set on
// ServiceBusMessage.MessageId when publishing) and as the idempotency key
// consumers use to detect redelivery. It is stable per quote: republishing
// or redelivering the same quote reuses the same value, so a subscriber
// that has already handled MessageId "quote-42-created" can safely skip a
// duplicate delivery.
public sealed record QuoteCreatedEvent(
    string MessageId,
    int QuoteId,
    string Author,
    string Text,
    DateTimeOffset CreatedAtUtc)
{
    // A quote created with this exact author value is a deliberate poison
    // message for the Day 19 demo: every subscriber throws when it sees
    // this event, so Service Bus's normal delivery-count tracking abandons
    // and redelivers it until the subscription's MaxDeliveryCount (3, set
    // on sub-a/sub-b) is exceeded and the broker moves it to the dead-letter
    // queue automatically. No application code fakes the DLQ.
    public const string PoisonAuthorMarker = "__day19_poison_test__";

    public static string BuildMessageId(int quoteId) =>
        $"quote-{quoteId}-created";
}
