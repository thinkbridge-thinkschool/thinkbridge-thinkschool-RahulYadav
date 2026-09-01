namespace QuotesApi.Models;

// Idempotency record for the Day 19 Service Bus consumers. Keyed by
// (SubscriptionName, MessageId) rather than MessageId alone because each
// subscription is an independent copy of the topic message — Subscription
// A and Subscription B each need to track their own "have I handled this
// MessageId" state.
//
// Persisted in the shared QuotesDbContext (SQLite) rather than kept in
// process memory: competing consumers on the same subscription run as
// separate BackgroundService instances that must agree on what has
// already been processed, and that has to survive a worker restart too.
public sealed class ProcessedMessage
{
    public string SubscriptionName { get; set; } = string.Empty;

    public string MessageId { get; set; } = string.Empty;

    public DateTimeOffset ProcessedAtUtc { get; set; }
}
