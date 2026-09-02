using Microsoft.Extensions.Logging;
using QuotesApi.BackgroundProcessing;
using QuotesApi.Messaging;
using QuotesApi.Models;
using QuotesApi.Repositories;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace QuotesApi.Extensions;

public static class QuoteEndpointExtensions
{
    private sealed record CreateQuoteRequest(string Author, string Text);

    public static IEndpointRouteBuilder MapQuoteEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quotes");

        group.MapGet("/", async (
            int? page,
            int? size,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            int currentPage = page ?? 1;
            int pageSize = size ?? 10;

            if (currentPage < 1 || pageSize < 1)
                return Results.BadRequest(
                    "Page and size must be greater than 0.");

            var quotes = await repository.GetQuotesAsync(
                currentPage,
                pageSize,
                cancellationToken);

            return Results.Ok(quotes);
        });

        group.MapGet("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var quote = await repository.GetByIdAsync(
                id,
                cancellationToken);

            return quote is null
                ? Results.NotFound()
                : Results.Ok(quote);
        });

        group.MapPost("/", async (
            CreateQuoteRequest request,
            ClaimsPrincipal user,
            IQuoteRepository repository,
            IQuoteProcessingQueue processingQueue,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.QuoteEndpoints");

            var creation = Quote.Create(
                request.Author,
                request.Text);

            if (!creation.IsSuccess)
                return Results.BadRequest(creation.Error);

            // Day 20: Transactional Outbox. The quote row and the
            // QuoteCreated outbox row are written in the SAME EF Core
            // transaction (see QuoteRepository.AddAsync) — there is no
            // longer a direct call to IQuoteEventPublisher here. Publishing
            // to Service Bus happens later, out of band, in
            // OutboxRelayBackgroundService, which is the only thing that
            // reads unsent outbox rows and the only thing that marks them
            // sent. This is what guarantees the database write and the
            // eventual topic publish cannot diverge: if this request
            // returns 201, the outbox row is already durable and WILL be
            // published (at least once), even if the process crashes the
            // instant after this call returns.
            var createdQuote = await repository.AddAsync(
                creation.Quote!,
                quote => BuildQuoteCreatedOutboxMessage(quote),
                cancellationToken);

            var userId =
                user.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

            logger.LogInformation(
                "Created quote {QuoteId} for user {UserId}",
                createdQuote.Id,
                userId);

            // Slow formatting/enrichment work happens off the request
            // thread — enqueue only, do not await the work itself here.
            await processingQueue.QueueQuoteForProcessingAsync(
                createdQuote.Id,
                cancellationToken);

            logger.LogInformation(
                "Enqueued quote {QuoteId} for background processing",
                createdQuote.Id);

            return Results.Created(
                $"/api/quotes/{createdQuote.Id}",
                createdQuote);
        })
        .RequireAuthorization();

        group.MapDelete("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var deleted = await repository.DeleteAsync(
                id,
                cancellationToken);

            return deleted
                ? Results.NoContent()
                : Results.NotFound();
        })
        .RequireAuthorization("can-edit-quotes");

        return app;
    }

    // Builds the outbox row for a just-created quote. Reuses QuoteCreatedEvent
    // (Day 19) as the payload schema rather than inventing a second one —
    // OutboxRelayBackgroundService deserializes this same type back out when
    // it publishes. MessageId is derived once, here, from the quote's Id and
    // stored on the row; it is never regenerated afterwards (see
    // OutboxMessage.MessageId), so every relay attempt for this row —
    // including retries after a crash — reuses the exact same Service Bus
    // MessageId the consumer's idempotency check keys on.
    private static OutboxMessage BuildQuoteCreatedOutboxMessage(Quote quote)
    {
        var quoteCreated = new QuoteCreatedEvent(
            QuoteCreatedEvent.BuildMessageId(quote.Id),
            quote.Id,
            quote.Author,
            quote.Text,
            DateTimeOffset.UtcNow);

        return new OutboxMessage
        {
            MessageId = quoteCreated.MessageId,
            EventType = QuoteCreatedEvent.EventType,
            Payload = JsonSerializer.Serialize(quoteCreated),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}