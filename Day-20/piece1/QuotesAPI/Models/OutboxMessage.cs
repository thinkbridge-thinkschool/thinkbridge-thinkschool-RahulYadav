namespace QuotesApi.Models;

// Day 20: Transactional Outbox row. Written in the SAME EF Core
// transaction/DbContext.SaveChangesAsync call as the domain change it
// describes (see QuoteRepository.AddAsync), so "the quote exists" and "the
// event that must eventually reach Service Bus exists" become one atomic
// database fact instead of two independent operations that could diverge.
//
// OutboxRelayBackgroundService is the only thing that ever publishes these
// rows and the only thing that sets SentAtUtc — and only after a successful
// publish (see Messaging/OutboxRelayBackgroundService.cs).
public sealed class OutboxMessage
{
    public int Id { get; set; }

    // Stable per business event (e.g. "quote-42-created" from
    // QuoteCreatedEvent.BuildMessageId). Set once, at insert time, and never
    // regenerated — the relay reuses this exact value as the Service Bus
    // MessageId on every publish attempt, including retries after a crash,
    // so the consumer's existing idempotency check (ProcessedMessage) can
    // recognize a redelivery.
    public string MessageId { get; set; } = string.Empty;

    // Discriminates the shape of Payload. Only "QuoteCreated" exists today
    // (see QuoteCreatedEvent), but the column exists so the outbox table
    // does not have to be reshaped the next time a different event needs
    // outbox delivery.
    public string EventType { get; set; } = string.Empty;

    // The serialized event (JSON), captured at the moment of the domain
    // change so the relay never needs to re-derive it later from
    // possibly-changed state.
    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    // Null until a publish attempt actually succeeds. Never set before
    // that — this is the field the whole pattern exists to protect.
    public DateTimeOffset? SentAtUtc { get; set; }

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }
}
