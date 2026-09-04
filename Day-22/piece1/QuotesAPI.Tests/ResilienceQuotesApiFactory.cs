using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Extensions;

namespace QuotesApi.Tests;

// Day 22: QuotesApiFactory variant used by the resilience tests. Boots the
// REAL Program.cs (real DI container, real AddQuoteDependencyResilience
// registration, real IQuoteDependencyClient), but swaps the "QuoteDependency"
// named HttpClient's PRIMARY handler for a deterministic fake (see
// FakeQuoteDependencyHandler) so no real network call ever happens. The
// Polly resilience pipelines themselves are never mocked -- this is the
// actual configured DI pipeline running against a controllable fake
// dependency.
//
// configOverrides lets each test tune Resilience:* thresholds (e.g. a lower
// CircuitBreaker:MinimumThroughput, a shorter BreakDurationSeconds) without
// touching appsettings.Testing.json's shared defaults, and gets a fresh
// pipeline/circuit-breaker state per factory instance.
internal sealed class ResilienceQuotesApiFactory : QuotesApiFactory
{
    private readonly Dictionary<string, string?> _configOverrides;

    public FakeQuoteDependencyHandler Handler { get; } = new();

    public ResilienceQuotesApiFactory(Dictionary<string, string?>? configOverrides = null)
    {
        _configOverrides = configOverrides ?? [];
    }

    protected override void ConfigureAdditionalConfiguration(IConfigurationBuilder config)
    {
        config.AddInMemoryCollection(_configOverrides);
    }

    protected override void ConfigureAdditionalTestServices(IServiceCollection services)
    {
        services
            .AddHttpClient(QuoteDependencyResilienceExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => Handler);
    }
}
