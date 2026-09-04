using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using QuotesApi.Resilience;
using Xunit.Abstractions;

namespace QuotesApi.Tests;

// Day 22, requirement 9.C/9.D: circuit breaker open + half-open recovery,
// proven against the real DI-registered "quote-dependency-post" pipeline
// (POST has no retry stage, so every request maps to exactly one dependency
// call/one circuit-breaker record -- the cleanest way to observe the state
// machine).
public sealed class QuoteDependencyCircuitBreakerTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ResilienceQuotesApiFactory _factory = null!;

    public QuoteDependencyCircuitBreakerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync()
    {
        _factory = new ResilienceQuotesApiFactory(new Dictionary<string, string?>
        {
            ["Resilience:CircuitBreaker:MinimumThroughput"] = "4",
            ["Resilience:CircuitBreaker:FailureRatio"] = "0.5",
            ["Resilience:CircuitBreaker:SamplingDurationSeconds"] = "5",
            ["Resilience:CircuitBreaker:BreakDurationSeconds"] = "0.6"
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // 9.C: Closed -> repeated failures -> Open, then a further request must
    // NOT reach the dependency at all.
    [Fact]
    public async Task SustainedFailures_OpenTheCircuit_ThenRejectWithoutCallingDependency()
    {
        _factory.Handler.Handle = (_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("simulated sustained failure"));

        using var scope = _factory.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IQuoteDependencyClient>();
        var metrics = scope.ServiceProvider.GetRequiredService<QuoteDependencyResilienceMetrics>();

        Assert.Equal("Closed", metrics.CircuitState);

        for (var i = 1; i <= 4; i++)
        {
            await Assert.ThrowsAsync<HttpRequestException>(
                () => client.SubmitQuoteAsync("quote", CancellationToken.None));

            _output.WriteLine($"Request {i}: dependency failure");
        }

        Assert.Equal("Open", metrics.CircuitState);
        Assert.Equal(1, metrics.CircuitOpenedCount);
        Assert.Equal(4, _factory.Handler.TotalCalls);

        var callsAtOpen = _factory.Handler.TotalCalls;

        await Assert.ThrowsAsync<BrokenCircuitException>(
            () => client.SubmitQuoteAsync("quote", CancellationToken.None));

        // The dependency must NOT have been called while the circuit is open.
        Assert.Equal(callsAtOpen, _factory.Handler.TotalCalls);

        _output.WriteLine($"Circuit state after {callsAtOpen} failures: {metrics.CircuitState}");
        _output.WriteLine("Further request rejected by circuit -- dependency was NOT called");
    }

    // 9.D: Open -> wait for BreakDuration -> Half-Open -> successful probe
    // -> Closed.
    [Fact]
    public async Task AfterBreakDuration_HalfOpenProbeSucceeds_AndCircuitCloses()
    {
        _factory.Handler.Handle = (_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("simulated sustained failure"));

        using var scope = _factory.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IQuoteDependencyClient>();
        var metrics = scope.ServiceProvider.GetRequiredService<QuoteDependencyResilienceMetrics>();

        for (var i = 1; i <= 4; i++)
        {
            await Assert.ThrowsAsync<HttpRequestException>(
                () => client.SubmitQuoteAsync("quote", CancellationToken.None));
        }

        Assert.Equal("Open", metrics.CircuitState);
        _output.WriteLine("Circuit OPEN after sustained failures. Waiting for break duration...");

        // BreakDurationSeconds is configured to 0.6s above; Polly requires
        // it to be > 0.5s, so this is already close to the practical
        // minimum for a fast, deterministic test.
        await Task.Delay(TimeSpan.FromSeconds(0.7));

        _factory.Handler.Handle = (_, _) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("Quote created")
            });

        var probeResult = await client.SubmitQuoteAsync("recovered quote", CancellationToken.None);

        Assert.Equal("Quote created", probeResult);
        Assert.Equal("Closed", metrics.CircuitState);
        Assert.Equal(1, metrics.CircuitHalfOpenedCount);
        Assert.Equal(1, metrics.CircuitClosedCount);

        _output.WriteLine($"Probe request: SUCCESS ({probeResult})");
        _output.WriteLine($"Circuit state after probe: {metrics.CircuitState}");
        _output.WriteLine("Recovery confirmed");
    }
}
