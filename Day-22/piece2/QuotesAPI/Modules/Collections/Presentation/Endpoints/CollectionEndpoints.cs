using QuotesApi.Modules.Collections.Application.Commands;
using QuotesApi.Modules.Collections.Application.Exceptions;
using QuotesApi.Modules.Collections.Application.Queries;

namespace QuotesApi.Modules.Collections.Presentation.Endpoints;

public static class CollectionEndpoints
{
    public static void MapCollectionsModuleEndpoints(this WebApplication app)
    {
        app.MapPost("/api/collections", CreateCollectionAsync);
        app.MapGet("/api/collections/{id:int}", GetCollectionAsync);
        app.MapPost("/api/collections/{id:int}/items", AddQuoteAsync);
        app.MapDelete("/api/collections/{id:int}/items/{quoteId:int}", RemoveQuoteAsync);
    }

    private static async Task<IResult> CreateCollectionAsync(
        CreateCollectionRequest request,
        CreateCollectionCommandHandler handler,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await handler.HandleAsync(
                new CreateCollectionCommand(request.Name, request.OwnerId),
                cancellationToken);

            return Results.Created($"/api/collections/{dto.Id}", dto);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    private static async Task<IResult> GetCollectionAsync(
        int id,
        GetCollectionQueryHandler handler,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await handler.HandleAsync(new GetCollectionQuery(id), cancellationToken);
            return Results.Ok(dto);
        }
        catch (CollectionNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> AddQuoteAsync(
        int id,
        AddQuoteRequest request,
        AddQuoteToCollectionCommandHandler handler,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await handler.HandleAsync(
                new AddQuoteToCollectionCommand(id, request.QuoteId),
                cancellationToken);

            return Results.Ok(dto);
        }
        catch (CollectionNotFoundException)
        {
            return Results.NotFound();
        }
        catch (QuoteNotFoundException ex)
        {
            return Results.BadRequest(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    private static async Task<IResult> RemoveQuoteAsync(
        int id,
        int quoteId,
        RemoveQuoteFromCollectionCommandHandler handler,
        CancellationToken cancellationToken)
    {
        try
        {
            await handler.HandleAsync(
                new RemoveQuoteFromCollectionCommand(id, quoteId),
                cancellationToken);

            return Results.NoContent();
        }
        catch (CollectionNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    public sealed record CreateCollectionRequest(string Name, int OwnerId);

    public sealed record AddQuoteRequest(int QuoteId);
}
