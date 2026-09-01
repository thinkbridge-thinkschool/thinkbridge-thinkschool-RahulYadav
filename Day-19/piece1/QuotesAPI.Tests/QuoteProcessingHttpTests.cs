using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuotesApi.BackgroundProcessing;

namespace QuotesApi.Tests;

// Confirms the actual requirement of this exercise: POST /api/quotes only
// enqueues background work and returns before that work has run — it does
// not perform the slow work synchronously on the request thread.
public sealed class QuoteProcessingHttpTests : IAsyncLifetime
{
    private QuotesApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new QuotesApiFactory();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreateQuote_EnqueuesForBackgroundProcessing_RatherThanProcessingInline()
    {
        var recordingQueue = new RecordingQuoteProcessingQueue();

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IQuoteProcessingQueue>();
                services.AddSingleton<IQuoteProcessingQueue>(recordingQueue);
            });
        });

        using var client = factory.CreateClient();
        var token = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new { author = "Katherine Johnson", text = "Background work belongs off the request thread." });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<QuoteDto>();

        // The request handler enqueued exactly the created quote — it never
        // touched a repository/formatter itself for the "slow" step.
        Assert.Single(recordingQueue.QueuedQuoteIds);
        Assert.Equal(created!.Id, recordingQueue.QueuedQuoteIds[0]);
    }

    [Fact]
    public async Task CreateQuote_ReturnsWellBeforeSimulatedBackgroundDelayElapses()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["QuoteProcessing:SimulatedWorkDelay"] = "00:00:02"
                });
            });
        });

        using var client = factory.CreateClient();
        var token = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var stopwatch = Stopwatch.StartNew();
        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new { author = "Grace Hopper", text = "The request thread does not wait for background work." });
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"Expected the request to return well under the 2s simulated background delay; took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task HostShutdown_DoesNotWaitOutInFlightSimulatedDelay()
    {
        // Exercises the exact DI registrations in Program.cs (real
        // QuoteProcessingBackgroundService, real singleton queue) end to
        // end, rather than a hand-built worker.
        //
        // Host shutdown has real fixed overhead unrelated to this worker
        // (e.g. OpenTelemetry's OTLP exporter — configured in Program.cs
        // against an unreachable localhost collector — has its own
        // shutdown/flush timeout). Asserting an absolute wall-clock bound
        // on disposal would be flaky against that unrelated overhead, so
        // this compares disposal WITH a long in-flight simulated delay
        // against a baseline WITHOUT one: if shutdown waited out the
        // delay, the difference would be ~20s; graceful cancellation
        // overhead should be a few seconds at most.
        var baselineElapsed = await MeasureShutdownElapsedAsync(
            simulatedWorkDelay: null);

        var withInFlightWorkElapsed = await MeasureShutdownElapsedAsync(
            simulatedWorkDelay: TimeSpan.FromSeconds(20));

        var extraTime = withInFlightWorkElapsed - baselineElapsed;

        Assert.True(
            extraTime < TimeSpan.FromSeconds(6),
            $"Shutdown with a 20s in-flight simulated delay took {extraTime} longer " +
            $"than the baseline ({withInFlightWorkElapsed} vs {baselineElapsed}); " +
            "expected only graceful-cancellation overhead, not the delay itself.");
    }

    private async Task<TimeSpan> MeasureShutdownElapsedAsync(TimeSpan? simulatedWorkDelay)
    {
        var factory = simulatedWorkDelay is { } delay
            ? _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["QuoteProcessing:SimulatedWorkDelay"] = delay.ToString()
                    });
                });
            })
            : _factory.WithWebHostBuilder(_ => { });

        using (var client = factory.CreateClient())
        {
            var token = await LoginAsync(client);
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);

            var response = await client.PostAsJsonAsync(
                "/api/quotes",
                new { author = "Shutdown Integration", text = "In-flight when the host stops." });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        // The quote above was just enqueued and the worker is now (or is
        // about to be) mid-way through the configured delay. Disposing the
        // factory triggers the same IHost.StopAsync() the production host
        // runs on SIGINT/SIGTERM.
        var stopwatch = Stopwatch.StartNew();
        factory.Dispose();
        stopwatch.Stop();

        return stopwatch.Elapsed;
    }

    private static async Task<string> LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "test@example.com", password = "Password123!" });

        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        return login!.AccessToken;
    }

    private sealed record QuoteDto(int Id, string Author, string Text, bool IsDeleted);

    private sealed record LoginResponseDto(string AccessToken, string RefreshToken, int ExpiresIn);

    // Records what the request path enqueues without ever completing a
    // read, standing in for "the worker hasn't gotten to it yet."
    private sealed class RecordingQuoteProcessingQueue : IQuoteProcessingQueue
    {
        public List<int> QueuedQuoteIds { get; } = new();

        public ValueTask QueueQuoteForProcessingAsync(int quoteId, CancellationToken cancellationToken)
        {
            QueuedQuoteIds.Add(quoteId);
            return ValueTask.CompletedTask;
        }

        public IAsyncEnumerable<int> DequeueAllAsync(CancellationToken cancellationToken)
            => NeverYieldsAsync(cancellationToken);

        private static async IAsyncEnumerable<int> NeverYieldsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }
    }
}
