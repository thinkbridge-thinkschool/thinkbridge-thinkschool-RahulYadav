using MediatR;
using Microsoft.Extensions.Logging;
using QuotesApi.Commands;
using QuotesApi.Models;
using QuotesApi.Queries;
using QuotesApi.Repositories;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace QuotesApi.Extensions;

public static class QuoteEndpointExtensions
{
    public static IEndpointRouteBuilder MapQuoteEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quotes");

        // ========================================================
        // EF CORE READ PATH
        // Query -> Query Handler -> Read Model
        // ========================================================

        group.MapGet("/", async (
            int? page,
            int? size,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            int currentPage = page ?? 1;
            int pageSize = size ?? 10;

            if (currentPage < 1 || pageSize < 1)
            {
                return Results.BadRequest(
                    "Page and size must be greater than 0.");
            }

            var query = new GetQuotesQuery(
                currentPage,
                pageSize);

            var quotes = await mediator.Send(
                query,
                cancellationToken);

            return Results.Ok(quotes);
        });

        // ========================================================
        // DAPPER READ PATH
        // Query -> Dapper Query Handler -> Read Model
        // ========================================================

        group.MapGet("/dapper", async (
            int? page,
            int? size,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            int currentPage = page ?? 1;
            int pageSize = size ?? 10;

            if (currentPage < 1 || pageSize < 1)
            {
                return Results.BadRequest(
                    "Page and size must be greater than 0.");
            }

            var query = new GetQuotesDapperQuery(
                currentPage,
                pageSize);

            var quotes = await mediator.Send(
                query,
                cancellationToken);

            return Results.Ok(quotes);
        });

        // ========================================================
        // GET SINGLE QUOTE
        // Existing repository path kept unchanged
        // ========================================================

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

        // ========================================================
        // WRITE PATH
        // Command -> Command Handler -> Entity -> Database
        // ========================================================

        group.MapPost("/", async (
            CreateQuoteCommand command,
            ClaimsPrincipal user,
            IMediator mediator,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger =
                loggerFactory.CreateLogger(
                    "QuotesApi.QuoteEndpoints");

            try
            {
                var quoteId = await mediator.Send(
                    command,
                    cancellationToken);

                var userId =
                    user.FindFirstValue(
                        JwtRegisteredClaimNames.Sub)
                    ?? user.FindFirstValue(
                        ClaimTypes.NameIdentifier);

                logger.LogInformation(
                    "Created quote {QuoteId} for user {UserId}",
                    quoteId,
                    userId);

                return Results.Created(
                    $"/api/quotes/{quoteId}",
                    new
                    {
                        id = quoteId
                    });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(
                    ex.Message);
            }
        })
        .RequireAuthorization();

        // ========================================================
        // DELETE
        // Existing repository path kept unchanged
        // ========================================================

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
}