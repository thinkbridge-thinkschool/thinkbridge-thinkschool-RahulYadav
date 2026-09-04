using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Polly.RateLimiting;
using QuotesApi.Resilience;
using Xunit.Abstractions;

namespace QuotesApi.Tests;

// Day 22, requirement 9.F: more concurrent calls than the configured
// concurrency limit are made; excess calls must be rejected immediately
// (fail fast) rather than queued or allowed to pile up against the
// dependency.
public sealed class QuoteDependencyBulkheadTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ResilienceQuotesApiFactory _factory = null!;

    public QuoteDependencyBulkheadTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync()
    {
        _factory = new ResilienceQuotesApiFactory(new Dictionary<string, string?>
        {
            ["Resilience:Bulkhead:MaxConcurrency"] = "2",
            ["Resilience:Bulkhead:QueueLimit"] = "0"
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task MoreConcurrentCallsThanLimit_ExcessCallsAreRejectedFast()
    {
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

        using var scope = _factory.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IQuoteDependencyClient>();
        var metrics = scope.ServiceProvider.GetRequiredService<QuoteDependencyResilienceMetrics>();

        const int concurrentRequests = 5;

        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => client.GetQuoteOfTheDayAsync(CancellationToken.None))
            .ToArray();

        // The concurrency limiter's accept/reject decision for each caller
        // above the limit is synchronous, so this delay is not needed for
        // correctness -- it just gives every task a chance to have started
        // before releasing the gate, matching the style used elsewhere in
        // this suite (see HybridCacheStampedeTests).
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

        Assert.Equal(2, maxObservedConcurrency);
        Assert.Equal(2, succeeded);
        Assert.Equal(3, rejected);
        Assert.Equal(3, metrics.BulkheadRejectedCount);

        _output.WriteLine("=== BULKHEAD EVIDENCE ===");
        _output.WriteLine($"Concurrent requests sent: {concurrentRequests}, limit: 2");
        _output.WriteLine($"Max concurrent in-flight dependency calls observed: {maxObservedConcurrency}");
        _output.WriteLine($"Succeeded: {succeeded}, Rejected: {rejected}");
        _output.WriteLine($"Recorded bulkhead rejections (metrics): {metrics.BulkheadRejectedCount}");
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
