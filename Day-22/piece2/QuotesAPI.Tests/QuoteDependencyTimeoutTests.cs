using Microsoft.Extensions.DependencyInjection;
using Polly.Timeout;
using QuotesApi.Resilience;
using Xunit.Abstractions;

namespace QuotesApi.Tests;

// Day 22, requirement 9.E: the dependency deliberately delays longer than
// the configured timeout, proven against the real DI-registered pipelines.
public sealed class QuoteDependencyTimeoutTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ResilienceQuotesApiFactory _factory = null!;

    public QuoteDependencyTimeoutTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync()
    {
        _factory = new ResilienceQuotesApiFactory(new Dictionary<string, string?>
        {
            ["Resilience:TimeoutSeconds"] = "0.2"
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // POST has no retry stage, so this isolates a single attempt's timeout
    // behavior cleanly: exactly one call is made, and it is cancelled once
    // the configured timeout elapses.
    [Fact]
    public async Task Post_DependencyDelayExceedsTimeout_IsCancelledAfterConfiguredTimeout()
    {
        _factory.Handler.Handle = async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        };

        using var scope = _factory.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IQuoteDependencyClient>();
        var metrics = scope.ServiceProvider.GetRequiredService<QuoteDependencyResilienceMetrics>();

        var started = DateTimeOffset.UtcNow;

        await Assert.ThrowsAsync<TimeoutRejectedException>(
            () => client.SubmitQuoteAsync("quote", CancellationToken.None));

        var elapsed = DateTimeOffset.UtcNow - started;

        Assert.Equal(1, _factory.Handler.TotalCalls);
        Assert.Equal(1, metrics.TimeoutCount);
        Assert.True(elapsed < TimeSpan.FromSeconds(2), $"Expected a fast timeout, took {elapsed}");

        _output.WriteLine("=== TIMEOUT EVIDENCE ===");
        _output.WriteLine($"Configured timeout: 0.2s; dependency delay: 5s");
        _output.WriteLine($"Call cancelled after: {elapsed.TotalMilliseconds:F0}ms");
        _output.WriteLine($"Recorded timeout events (metrics): {metrics.TimeoutCount}");
    }

    // GET has a retry stage: each attempt gets its own timeout budget, so a
    // dependency that always hangs is retried MaxRetryAttempts times (each
    // one individually timing out) before the final TimeoutRejectedException
    // surfaces -- proving timeout, retry and circuit breaker interact
    // correctly (timeout is innermost, so it bounds each attempt rather than
    // the whole retry loop).
    [Fact]
    public async Task Get_DependencyAlwaysDelaysPastTimeout_RetriesEachAttemptThenFails()
    {
        // This scenario needs different Retry/CircuitBreaker overrides than
        // the default instance created in InitializeAsync.
        _factory.Dispose();

        _factory = new ResilienceQuotesApiFactory(new Dictionary<string, string?>
        {
            ["Resilience:TimeoutSeconds"] = "0.1",
            ["Resilience:Retry:MaxRetryAttempts"] = "2",
            ["Resilience:Retry:BackoffSeconds"] = "0.02",
            ["Resilience:CircuitBreaker:MinimumThroughput"] = "100"
        });

        _factory.Handler.Handle = async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        };

        using var scope = _factory.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IQuoteDependencyClient>();
        var metrics = scope.ServiceProvider.GetRequiredService<QuoteDependencyResilienceMetrics>();

        await Assert.ThrowsAsync<TimeoutRejectedException>(
            () => client.GetQuoteOfTheDayAsync(CancellationToken.None));

        // 1 initial attempt + 2 retries, every single one timing out.
        Assert.Equal(3, _factory.Handler.TotalCalls);
        Assert.Equal(3, metrics.TimeoutCount);
        Assert.Equal(2, metrics.RetryAttempts);

        _output.WriteLine("=== TIMEOUT + RETRY INTERACTION EVIDENCE ===");
        _output.WriteLine($"Attempts made (all individually timed out): {_factory.Handler.TotalCalls}");
        _output.WriteLine($"Recorded timeout events: {metrics.TimeoutCount}");
        _output.WriteLine($"Recorded retry attempts: {metrics.RetryAttempts}");
    }
}
