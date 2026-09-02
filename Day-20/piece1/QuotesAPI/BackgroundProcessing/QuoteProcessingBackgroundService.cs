using Microsoft.Extensions.Options;
using QuotesApi.Options;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.BackgroundProcessing;

// Drains QuoteProcessingQueue continuously for the lifetime of the host.
// This replaces what would otherwise be slow, synchronous post-creation
// work (formatting/enrichment against a quote) running inline on the HTTP
// request thread.
//
// Public so tests can exercise it directly via IHostedService.StartAsync/
// StopAsync — the same lifecycle ASP.NET Core's host uses — rather than
// reaching into implementation details.
public sealed class QuoteProcessingBackgroundService : BackgroundService
{
    private readonly IQuoteProcessingQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QuoteProcessingBackgroundService> _logger;
    private readonly TimeSpan _simulatedWorkDelay;

    public QuoteProcessingBackgroundService(
        IQuoteProcessingQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<QuoteProcessingBackgroundService> logger,
        IOptions<QuoteProcessingOptions> options)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _simulatedWorkDelay = options.Value.SimulatedWorkDelay;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Quote processing background worker starting.");

        try
        {
            await foreach (var quoteId in _queue.DequeueAllAsync(stoppingToken))
            {
                await ProcessQuoteAsync(quoteId, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected: host shutdown requested cancellation while the
            // worker was waiting on an empty queue. Not an error.
            _logger.LogInformation(
                "Quote processing background worker cancellation requested; " +
                "exiting the queue read loop.");
        }

        _logger.LogInformation("Quote processing background worker stopped.");
    }

    private async Task ProcessQuoteAsync(int quoteId, CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Dequeued quote {QuoteId} for background processing.",
            quoteId);

        try
        {
            // A new DI scope per work item: IQuoteRepository is scoped
            // (it holds a DbContext) and the background service itself is
            // a singleton, so it cannot depend on scoped services directly.
            using var scope = _scopeFactory.CreateScope();

            var repository = scope.ServiceProvider
                .GetRequiredService<IQuoteRepository>();

            var formatter = scope.ServiceProvider
                .GetRequiredService<QuoteFormatter>();

            var quote = await repository.GetByIdAsync(quoteId, stoppingToken);

            if (quote is null)
            {
                _logger.LogWarning(
                    "Quote {QuoteId} was not found; skipping background processing.",
                    quoteId);

                return;
            }

            // Simulated slow work. Passing stoppingToken means a shutdown
            // interrupts an in-flight delay instead of the worker sitting
            // through it.
            await Task.Delay(_simulatedWorkDelay, stoppingToken);

            var formatted = formatter.Format(quote.Text);

            _logger.LogInformation(
                "Completed background processing for quote {QuoteId} " +
                "(formatted length {Length}).",
                quoteId,
                formatted.Length);
        }
        catch (OperationCanceledException)
        {
            // Let the host-driven cancellation propagate to the outer
            // read loop rather than being reported as a work-item failure.
            throw;
        }
        catch (Exception ex)
        {
            // A single bad work item must not take down the worker — the
            // next queued quote should still be processed.
            _logger.LogError(
                ex,
                "Background processing failed for quote {QuoteId}.",
                quoteId);
        }
    }
}
