namespace QuotesApi.Options;

public sealed class QuoteProcessingOptions
{
    // Stands in for a real slow dependency (search indexing, moderation
    // call, etc.). Configurable rather than hardcoded so tests can use a
    // short delay without waiting on the production value.
    public TimeSpan SimulatedWorkDelay { get; set; } = TimeSpan.FromSeconds(2);
}
