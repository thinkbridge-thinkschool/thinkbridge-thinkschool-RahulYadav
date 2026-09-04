using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Options;
using QuotesApi.Services;

namespace QuotesApi.Messaging;

// Day 20: Transactional Outbox relay.
//
// POST /api/quotes (see QuoteEndpointExtensions) no longer publishes to
// Service Bus itself — it only writes an OutboxMessage row, atomically with
// the quote, inside one EF Core transaction (QuoteRepository.AddAsync).
// This background service is the ONLY thing that ever reads unsent rows,
// publishes them, and marks them sent. That single-writer design is what
// makes the guarantee hold: once the HTTP request's transaction commits,
// the message exists durably and this relay WILL eventually publish it,
// independent of whatever happened to the request or the process
// afterwards.
//
// Delivery semantics are deliberately AT-LEAST-ONCE, not exactly-once: if
// the process crashes after a successful publish but before SentAtUtc is
// saved (see the crash-injection seam below), the row is still unsent on
// the next poll and gets published again under the same MessageId. The Day
// 19 consumer-side idempotency (ProcessedMessage / IProcessedMessageStore)
// is what makes that duplicate safe — this relay does not try to prevent
// the duplicate, only to never lose the message.
//
// Structurally this mirrors QuoteProcessingBackgroundService (Day 18) and
// ServiceBusSubscriptionWorker (Day 19): a singleton hosted service that
// opens a new DI scope per unit of work, because the scoped QuotesDbContext
// and IQuoteEventPublisher cannot be injected into a singleton directly.
public sealed class OutboxRelayBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxRelayBackgroundService> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly int _batchSize;

    public OutboxRelayBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxRelayBackgroundService> logger,
        IOptions<OutboxRelayOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pollInterval = options.Value.PollInterval;
        _batchSize = options.Value.BatchSize;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox relay starting.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await RelayPendingBatchAsync(stoppingToken);

                await Task.Delay(_pollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on host shutdown.
        }

        _logger.LogInformation("Outbox relay stopped.");
    }

    private async Task RelayPendingBatchAsync(CancellationToken stoppingToken)
    {
        try
        {
            // A new scope per poll: QuotesDbContext and IQuoteEventPublisher
            // are scoped, this hosted service is a singleton.
            using var scope = _scopeFactory.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

            // Ordered by Id (insertion order), not CreatedAtUtc: SQLite
            // cannot translate an ORDER BY over a DateTimeOffset column,
            // and Id/autoincrement already reflects the same ordering.
            var unsent = await db.OutboxMessages
                .Where(x => x.SentAtUtc == null)
                .OrderBy(x => x.Id)
                .Take(_batchSize)
                .ToListAsync(stoppingToken);

            if (unsent.Count == 0)
                return;

            var publisher = scope.ServiceProvider.GetRequiredService<IQuoteEventPublisher>();
            var crashInjector = scope.ServiceProvider.GetRequiredService<IOutboxCrashInjector>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            foreach (var message in unsent)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                await ProcessMessageAsync(db, publisher, crashInjector, clock, message, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A poll-level failure (e.g. the database is briefly
            // unavailable) must not take the relay down — the next poll,
            // after the delay, tries again. Unsent rows are untouched, so
            // nothing is lost.
            _logger.LogError(ex, "Outbox relay poll failed; will retry on the next interval.");
        }
    }

    private async Task ProcessMessageAsync(
        QuotesDbContext db,
        IQuoteEventPublisher publisher,
        IOutboxCrashInjector crashInjector,
        IClock clock,
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            var quoteCreated = DeserializePayload(message);

            // The outbox row's MessageId is the one true idempotency key —
            // it is set once, at insert time (QuoteEndpointExtensions), and
            // never regenerated. Every attempt for this row, including
            // retries after a crash, republishes under this exact value
            // rather than trusting whatever MessageId happens to be
            // embedded in the deserialized payload, even though today the
            // two are always equal by construction.
            var eventToPublish = quoteCreated with { MessageId = message.MessageId };

            await publisher.PublishQuoteCreatedAsync(eventToPublish, cancellationToken);

            // --------------------------------------------------------
            // Crash-injection seam (see IOutboxCrashInjector). In
            // production this is a no-op. In the crash-safety test, this
            // throws OutboxCrashSimulationException here to prove the row
            // survives with SentAtUtc still null and gets retried.
            // --------------------------------------------------------
            crashInjector.AfterPublishBeforeMarkSent(message);

            // Only reached if the publish succeeded AND no crash was
            // simulated. Never mark a row sent before this point.
            message.AttemptCount++;
            message.SentAtUtc = clock.UtcNow;
            message.LastError = null;

            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Relayed outbox message {MessageId} (attempt {AttemptCount}).",
                message.MessageId,
                message.AttemptCount);
        }
        catch (OutboxCrashSimulationException ex)
        {
            // Simulates the process dying between a successful publish and
            // the SaveChangesAsync above: nothing about this row is
            // persisted — not SentAtUtc, not AttemptCount, not LastError —
            // exactly as a real crash would leave it. The row stays unsent
            // and the next poll retries it under the same MessageId.
            _logger.LogWarning(
                ex,
                "Simulated crash after publishing outbox message {MessageId}; " +
                "SentAtUtc was NOT persisted. It will be retried.",
                message.MessageId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A genuine publish/deserialize failure: keep SentAtUtc null,
            // record the attempt and the error, and let the next poll
            // retry. This is the FAILURE branch of the pattern — the row
            // is never lost, only left for later.
            message.AttemptCount++;
            message.LastError = ex.Message;

            await db.SaveChangesAsync(cancellationToken);

            _logger.LogError(
                ex,
                "Failed to relay outbox message {MessageId} (attempt {AttemptCount}); will retry.",
                message.MessageId,
                message.AttemptCount);
        }
    }

    private static QuoteCreatedEvent DeserializePayload(OutboxMessage message)
    {
        if (message.EventType != QuoteCreatedEvent.EventType)
        {
            throw new InvalidOperationException(
                $"Outbox message {message.MessageId} has unsupported EventType " +
                $"'{message.EventType}'.");
        }

        return JsonSerializer.Deserialize<QuoteCreatedEvent>(message.Payload)
            ?? throw new InvalidOperationException(
                $"Outbox message {message.MessageId} payload could not be " +
                $"deserialized as {nameof(QuoteCreatedEvent)}.");
    }
}
