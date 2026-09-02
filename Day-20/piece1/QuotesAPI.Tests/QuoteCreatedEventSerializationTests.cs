using System.Text.Json;
using QuotesApi.Messaging;

namespace QuotesApi.Tests;

// Day 20: the outbox Payload column is exactly this serialized form (see
// QuoteEndpointExtensions.BuildQuoteCreatedOutboxMessage), and
// OutboxRelayBackgroundService deserializes it back before publishing. If
// this round trip were lossy or non-deterministic, a relayed message could
// diverge from what was originally committed to the database.
public sealed class QuoteCreatedEventSerializationTests
{
    [Fact]
    public void SerializeThenDeserialize_RoundTripsToAnEqualEvent()
    {
        var original = new QuoteCreatedEvent(
            QuoteCreatedEvent.BuildMessageId(42),
            42,
            "Katherine Johnson",
            "Get the numbers right.",
            new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));

        var payload = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<QuoteCreatedEvent>(payload);

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void Serialize_IsDeterministic_AcrossRepeatedCalls()
    {
        var quoteCreated = new QuoteCreatedEvent(
            "quote-7-created",
            7,
            "Alan Turing",
            "Machines take me by surprise.",
            new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));

        var first = JsonSerializer.Serialize(quoteCreated);
        var second = JsonSerializer.Serialize(quoteCreated);

        Assert.Equal(first, second);
    }

    [Fact]
    public void BuildMessageId_IsStable_ForTheSameQuoteId()
    {
        // The relay must reuse the exact same MessageId on every retry of
        // the same outbox row — this is only true if BuildMessageId is a
        // pure function of the quote id.
        Assert.Equal(
            QuoteCreatedEvent.BuildMessageId(123),
            QuoteCreatedEvent.BuildMessageId(123));
    }
}
