namespace QuotesApi.Options;

// Day 21: bound from the "QuoteCache" configuration section. Production
// (appsettings.json) uses the 30s defaults below; appsettings.Testing.json
// overrides both to ~1s so the cache-expiration test can observe a real
// TTL expiry without waiting out a 30-second delay.
public sealed class QuoteCacheOptions
{
    public TimeSpan Expiration { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan LocalCacheExpiration { get; set; } = TimeSpan.FromSeconds(30);
}
