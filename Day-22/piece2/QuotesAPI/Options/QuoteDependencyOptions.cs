namespace QuotesApi.Options;

// Day 22: base address for the outbound QuoteDependency call (see
// Resilience/QuoteDependencyClient.cs). This is a hostname, not a secret, so
// it lives in appsettings.json like other non-sensitive endpoints. The
// primary HTTP handler is swapped for a deterministic fake in tests, so this
// URL is never actually dialed outside Production/Development.
public sealed class QuoteDependencyOptions
{
    public string BaseUrl { get; set; } = "https://quote-dependency.internal/";
}
