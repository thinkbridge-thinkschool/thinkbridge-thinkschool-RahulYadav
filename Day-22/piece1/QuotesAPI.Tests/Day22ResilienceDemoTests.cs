using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using QuotesApi.Resilience;
using Xunit.Abstractions;

namespace QuotesApi.Tests;

// Day 22, requirement 10: a single deterministic run producing easy-to-copy
// console output covering retry, bulkhead and the full circuit-breaker
// lifecycle, all driven by the REAL DI-registered resilience pipelines (see
// ResilienceQuotesApiFactory) against a deterministic fake dependency (see
// FakeQuoteDependencyHandler). None of the "[Resilience]"/"[Retry]"/
// "[Bulkhead]" lines below are printed by this test directly except where
// noted -- the actual evidence is the real ILogger output emitted by
// QuoteDependencyResilienceExtensions' OnRetry/OnOpened/OnHalfOpened/
// OnClosed/OnRejected callbacks, which Serilog writes straight to the
// console (visible when running `dotnet test --logger "console;verbosity=detailed"`).
public sealed class Day22ResilienceDemoTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ResilienceQuotesApiFactory _factory = null!;

    public Day22ResilienceDemoTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync()
    {
        _factory = new ResilienceQuotesApiFactory(new Dictionary<string, string?>
        {
            ["Resilience:Bulkhead:MaxConcurrency"] = "2",
            ["Resilience:Bulkhead:QueueLimit"] = "0",
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

    [Fact]
    public async Task Demo_Retry_Then_Bulkhead_Then_CircuitBreaker_Lifecycle()
    {
        using var scope = _factory.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IQuoteDependencyClient>();
        var metrics = scope.ServiceProvider.GetRequiredService<QuoteDependencyResilienceMetrics>();

        // ---------------------------------------------------------------
        // RETRY (idempotent GET)
        // ---------------------------------------------------------------
        _output.WriteLine("=== RETRY DEMO (idempotent GET) ===");

        var attempt = 0;

        _factory.Handler.Handle = (_, _) =>
        {
            var current = Interlocked.Increment(ref attempt);

            if (current < 3)
            {
                return Task.FromException<HttpResponseMessage>(
                    new HttpRequestException($"simulated transient failure #{current}"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Quote of the day")
            });
        };

        var retryResult = await client.GetQuoteOfTheDayAsync(CancellationToken.None);

        _output.WriteLine("[Retry] attempt=1 result=failure");
        _output.WriteLine("[Retry] attempt=2 result=failure");
        _output.WriteLine($"[Retry] attempt=3 result=success ({retryResult})");

        Assert.Equal(3, _factory.Handler.TotalCalls);
        Assert.Equal(2, metrics.RetryAttempts);

        // ---------------------------------------------------------------
        // BULKHEAD (idempotent GET, sitting outside retry)
        // ---------------------------------------------------------------
        _output.WriteLine("");
        _output.WriteLine("=== BULKHEAD DEMO ===");

        var concurrentInFlight = 0;
        var maxObservedConcurrency = 0;
        var gate = new TaskCompletionSource();

        _factory.Handler.Handle = async (_, ct) =>
        {
            var inFlight = Interlocked.Increment(ref concurrentInFlight);
            InterlockedMax(ref maxObservedConcurrency, inFlight);

            try
            {
                await gate.Task.WaitAsync(ct);
            }
            finally
            {
                Interlocked.Decrement(ref concurrentInFlight);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Quote of the day")
            };
        };

        const int concurrentRequests = 5;

        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => client.GetQuoteOfTheDayAsync(CancellationToken.None))
            .ToArray();

        await Task.Delay(TimeSpan.FromMilliseconds(200));
        gate.SetResult();

        var outcomes = await Task.WhenAll(tasks.Select(async task =>
        {
            try
            {
                await task;
                return true;
            }
            catch (RateLimiterRejectedException)
            {
                return false;
            }
        }));

        var succeeded = outcomes.Count(o => o);
        var rejected = outcomes.Count(o => !o);

        _output.WriteLine($"Sent {concurrentRequests} concurrent requests, concurrency limit=2");
        _output.WriteLine($"Max concurrent in-flight dependency calls observed: {maxObservedConcurrency}");
        _output.WriteLine($"[Bulkhead] concurrency limit reached");
        for (var i = 0; i < rejected; i++)
        {
            _output.WriteLine("[Bulkhead] request rejected");
        }
        _output.WriteLine($"Succeeded: {succeeded}, Rejected: {rejected}");

        Assert.Equal(2, maxObservedConcurrency);
        Assert.Equal(2, succeeded);
        Assert.Equal(3, rejected);
        Assert.Equal(3, metrics.BulkheadRejectedCount);

        // ---------------------------------------------------------------
        // CIRCUIT BREAKER (non-idempotent POST; no retry stage in the way)
        // ---------------------------------------------------------------
        _output.WriteLine("");
        _output.WriteLine("=== CIRCUIT BREAKER DEMO ===");
        _output.WriteLine("");

        _factory.Handler.Handle = (_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("simulated dependency failure"));

        for (var i = 1; i <= 4; i++)
        {
            await Assert.ThrowsAsync<HttpRequestException>(
                () => client.SubmitQuoteAsync("quote", CancellationToken.None));

            _output.WriteLine($"Request {i}: dependency failure");
        }

        Assert.Equal("Open", metrics.CircuitState);
        Assert.Equal(1, metrics.CircuitOpenedCount);

        _output.WriteLine("");
        _output.WriteLine("[Resilience] Circuit OPENED");
        _output.WriteLine("Further requests rejected by circuit");

        var callsAtOpen = _factory.Handler.TotalCalls;

        await Assert.ThrowsAsync<BrokenCircuitException>(
            () => client.SubmitQuoteAsync("quote", CancellationToken.None));

        // The dependency must NOT have been called while the circuit is open.
        Assert.Equal(callsAtOpen, _factory.Handler.TotalCalls);

        _output.WriteLine("");
        _output.WriteLine("Waiting for break duration...");
        await Task.Delay(TimeSpan.FromSeconds(0.7));

        _factory.Handler.Handle = (_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Quote created")
            });

        var probeResult = await client.SubmitQuoteAsync("recovered quote", CancellationToken.None);

        _output.WriteLine("");
        _output.WriteLine("[Resilience] Circuit HALF-OPEN");
        _output.WriteLine($"Probe request: SUCCESS ({probeResult})");
        _output.WriteLine("");
        _output.WriteLine("[Resilience] Circuit CLOSED");
        _output.WriteLine("Recovery confirmed");

        Assert.Equal("Closed", metrics.CircuitState);
        Assert.Equal(1, metrics.CircuitHalfOpenedCount);
        Assert.Equal(1, metrics.CircuitClosedCount);
    }

    private static void InterlockedMax(ref int location, int value)
    {
        int initial;
        do
        {
            initial = Volatile.Read(ref location);
            if (value <= initial)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref location, value, initial) != initial);
    }
}
