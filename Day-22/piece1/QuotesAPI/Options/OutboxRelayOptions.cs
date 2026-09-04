namespace QuotesApi.Options;

// Day 20: configuration for OutboxRelayBackgroundService.
public sealed class OutboxRelayOptions
{
    // How long the relay waits between polling for unsent outbox rows.
    // Configurable so tests can drive it far faster than production without
    // busy-looping the database.
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    // Maximum unsent rows fetched per poll. Keeps a single poll bounded
    // even if a long Service Bus outage lets the table build up a backlog.
    public int BatchSize { get; set; } = 20;
}
