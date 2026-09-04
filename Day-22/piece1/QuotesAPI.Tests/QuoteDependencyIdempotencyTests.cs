using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Resilience;
using Xunit.Abstractions;

namespace QuotesApi.Tests;

// Day 22, requirement 9.A/9.B: proves the idempotency rule end to end
// against the REAL DI-registered resilience pipelines (see
// ResilienceQuotesApiFactory) -- not a hand-rolled Polly pipeline built just
// for the test.
public sealed class QuoteDependencyIdempotencyTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ResilienceQuotesApiFactory _factory = null!;

    public QuoteDependencyIdempotencyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync()
    {
        _factory = new ResilienceQuotesApiFactory();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // 9.A: idempotent GET fails twice, then succeeds on the 3rd attempt.
    [Fact]
    public async Task Get_TransientFailureThenSuccess_IsRetriedUntilSuccess()
    {
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
                Content = new StringContent("Quote of the day: perseverance.")
            });
        };

        using var scope = _factory.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IQuoteDependencyClient>();
        var metrics = scope.ServiceProvider.GetRequiredService<QuoteDependencyResilienceMetrics>();

        var result = await client.GetQuoteOfTheDayAsync(CancellationToken.None);

        Assert.Equal("Quote of the day: perseverance.", result);
        Assert.Equal(3, _factory.Handler.TotalCalls);
        Assert.Equal(2, metrics.RetryAttempts);

        _output.WriteLine("=== RETRY EVIDENCE (idempotent GET) ===");
        _output.WriteLine("attempt 1 -> failure");
        _output.WriteLine("wait/backoff");
        _output.WriteLine("attempt 2 -> failure");
        _output.WriteLine("wait/backoff");
        _output.WriteLine($"attempt 3 -> success ({result})");
        _output.WriteLine($"Total dependency calls: {_factory.Handler.TotalCalls}");
        _output.WriteLine($"Recorded retry attempts (metrics): {metrics.RetryAttempts}");
    }

    // 9.B: non-idempotent POST fails -- must be attempted exactly once, with
    // NO automatic retry.
    [Fact]
    public async Task Post_Failure_IsAttemptedExactlyOnceAndNotRetried()
    {
        _factory.Handler.Handle = (_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("simulated create failure"));

        using var scope = _factory.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IQuoteDependencyClient>();
        var metrics = scope.ServiceProvider.GetRequiredService<QuoteDependencyResilienceMetrics>();

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SubmitQuoteAsync("New quote", CancellationToken.None));

        Assert.Equal(1, _factory.Handler.TotalCalls);
        Assert.Equal(0, metrics.RetryAttempts);

        _output.WriteLine("=== IDEMPOTENCY EVIDENCE (non-idempotent POST) ===");
        _output.WriteLine($"Total dependency calls: {_factory.Handler.TotalCalls} (expected exactly 1)");
        _output.WriteLine($"Recorded retry attempts (metrics): {metrics.RetryAttempts} (expected 0 -- POST is never retried)");
    }
}
