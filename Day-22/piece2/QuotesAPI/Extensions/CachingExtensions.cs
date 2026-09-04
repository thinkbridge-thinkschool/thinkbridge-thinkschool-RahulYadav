using Microsoft.Extensions.Caching.Hybrid;
using QuotesApi.Caching;
using QuotesApi.Options;

namespace QuotesApi.Extensions;

// Day 21: HybridCache (L1 in-process memory + L2 Redis) for the hot quote
// read path. Mirrors the conditional-registration pattern already used for
// Service Bus in Program.cs: the Redis connection string is read from
// configuration only, never hardcoded, and when it is absent (Testing, or
// a local machine without Redis) HybridCache is still registered and still
// gives full in-process stampede protection — it simply runs with only the
// L1 tier, since no IDistributedCache was registered for it to use as L2.
// Production supplies ConnectionStrings:Redis (e.g. via Key Vault/App
// Settings) to light up the L2 tier without any code change here.
public static class CachingExtensions
{
    public static IServiceCollection AddQuoteCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnectionString = configuration.GetConnectionString("Redis");

        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = "QuotesApi:";
            });
        }

        var cacheOptions =
            configuration.GetSection("QuoteCache").Get<QuoteCacheOptions>()
            ?? new QuoteCacheOptions();

        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = cacheOptions.Expiration,
                LocalCacheExpiration = cacheOptions.LocalCacheExpiration
            };

            // Quotes are small (Author <= 200 chars, Text <= 1000 chars);
            // this is a generous ceiling, not a tuned production limit.
            options.MaximumPayloadBytes = 64 * 1024;
        });

        services.AddSingleton<QuoteCacheMetrics>();
        services.AddScoped<QuoteCacheReader>();

        return services;
    }
}
