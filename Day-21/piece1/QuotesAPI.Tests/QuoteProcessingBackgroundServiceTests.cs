using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuotesApi.BackgroundProcessing;
using QuotesApi.Options;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Tests;

// Exercises QuoteProcessingBackgroundService directly through the same
// IHostedService.StartAsync/StopAsync lifecycle the real ASP.NET Core host
// drives, against a fake repository so no database/HTTP pipeline is needed.
public sealed class QuoteProcessingBackgroundServiceTests
{
    private static readonly TimeSpan TestDelay = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private static (QuoteProcessingBackgroundService Worker, QuoteProcessingQueue Queue, FakeQuoteRepository Repository)
        CreateWorker(TimeSpan? delay = null)
    {
        var repository = new FakeQuoteRepository();

        var provider = new ServiceCollection()
            .AddScoped<IQuoteRepository>(_ => repository)
            .AddTransient<QuoteFormatter>()
            .BuildServiceProvider();

        var queue = new QuoteProcessingQueue();

        var worker = new QuoteProcessingBackgroundService(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<QuoteProcessingBackgroundService>.Instance,
            Microsoft.Extensions.Options.Options.Create(
                new QuoteProcessingOptions { SimulatedWorkDelay = delay ?? TestDelay }));

        return (worker, queue, repository);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met within the timeout.");

            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesSingleQueuedQuote()
    {
        var (worker, queue, repository) = CreateWorker();
        var quote = repository.Seed("Ada Lovelace", "The Analytical Engine.");

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await queue.QueueQuoteForProcessingAsync(quote.Id, CancellationToken.None);

            await WaitUntilAsync(
                () => repository.GetByIdRequests.Contains(quote.Id),
                WaitTimeout);
        }
        finally
        {
            using var stopCts = new CancellationTokenSource(WaitTimeout);
            await worker.StopAsync(stopCts.Token);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesMultipleQueuedQuotes_InOrder()
    {
        var (worker, queue, repository) = CreateWorker();
        var first = repository.Seed("Grace Hopper", "Ask forgiveness, not permission.");
        var second = repository.Seed("Alan Turing", "Machines take me by surprise.");
        var third = repository.Seed("Katherine Johnson", "Get the numbers right.");

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await queue.QueueQuoteForProcessingAsync(first.Id, CancellationToken.None);
            await queue.QueueQuoteForProcessingAsync(second.Id, CancellationToken.None);
            await queue.QueueQuoteForProcessingAsync(third.Id, CancellationToken.None);

            await WaitUntilAsync(
                () => repository.GetByIdRequests.Count >= 3,
                WaitTimeout);

            Assert.Equal(
                new[] { first.Id, second.Id, third.Id },
                repository.GetByIdRequests);
        }
        finally
        {
            using var stopCts = new CancellationTokenSource(WaitTimeout);
            await worker.StopAsync(stopCts.Token);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WorkItemFailure_DoesNotStopSubsequentProcessing()
    {
        var (worker, queue, repository) = CreateWorker();
        var failing = repository.Seed("Broken Author", "This lookup will throw.");
        repository.FailOnGetById.Add(failing.Id);
        var healthy = repository.Seed("Healthy Author", "This one should still process.");

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await queue.QueueQuoteForProcessingAsync(failing.Id, CancellationToken.None);
            await queue.QueueQuoteForProcessingAsync(healthy.Id, CancellationToken.None);

            // Proves the worker survived the failing item: the item queued
            // right after it still gets processed.
            await WaitUntilAsync(
                () => repository.GetByIdRequests.Contains(healthy.Id),
                WaitTimeout);

            Assert.Contains(failing.Id, repository.GetByIdRequests);
        }
        finally
        {
            using var stopCts = new CancellationTokenSource(WaitTimeout);
            await worker.StopAsync(stopCts.Token);
        }
    }

    [Fact]
    public async Task ExecuteAsync_MissingQuote_SkipsItAndContinuesWithNextItem()
    {
        var (worker, queue, repository) = CreateWorker();
        var healthy = repository.Seed("Present Author", "This quote exists.");

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await queue.QueueQuoteForProcessingAsync(999_999, CancellationToken.None);
            await queue.QueueQuoteForProcessingAsync(healthy.Id, CancellationToken.None);

            await WaitUntilAsync(
                () => repository.GetByIdRequests.Contains(healthy.Id),
                WaitTimeout);
        }
        finally
        {
            using var stopCts = new CancellationTokenSource(WaitTimeout);
            await worker.StopAsync(stopCts.Token);
        }
    }

    [Fact]
    public async Task StopAsync_StopsPromptly_EvenWhileAwaitingSimulatedWork()
    {
        // A long simulated delay stands in for slow in-flight work — a
        // graceful shutdown must interrupt it, not wait it out.
        var (worker, queue, repository) = CreateWorker(delay: TimeSpan.FromSeconds(30));
        var quote = repository.Seed("Slow Author", "Still being processed when shutdown happens.");

        await worker.StartAsync(CancellationToken.None);
        await queue.QueueQuoteForProcessingAsync(quote.Id, CancellationToken.None);

        await WaitUntilAsync(
            () => repository.GetByIdRequests.Contains(quote.Id),
            WaitTimeout);

        var stopwatch = Stopwatch.StartNew();
        using var stopCts = new CancellationTokenSource(WaitTimeout);
        await worker.StopAsync(stopCts.Token);
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"StopAsync took {stopwatch.Elapsed}; the worker should not wait out the 30s simulated delay.");
    }

    [Fact]
    public async Task StopAsync_DoesNotHang_WhenQueueIsIdle()
    {
        var (worker, _, _) = CreateWorker();

        await worker.StartAsync(CancellationToken.None);

        using var stopCts = new CancellationTokenSource(WaitTimeout);
        await worker.StopAsync(stopCts.Token);
    }
}
