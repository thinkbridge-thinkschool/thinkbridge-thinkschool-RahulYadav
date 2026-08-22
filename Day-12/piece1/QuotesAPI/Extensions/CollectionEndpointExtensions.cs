using Microsoft.AspNetCore.Http;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Extensions;

public static class CollectionEndpointExtensions
{
    public static void MapCollectionEndpoints(this WebApplication app)
    {
        // Create collection
        app.MapPost("/api/collections",
            async (
                CreateCollectionRequest request,
                ICollectionRepository repository,
                CancellationToken cancellationToken) =>
            {
                var collection = new Collection(
                    request.Name,
                    request.OwnerId);

                await repository.Add(collection, cancellationToken);

                return Results.Created(
                    $"/api/collections/{collection.Id}",
                    collection);
            });

        // Add quote to collection
        app.MapPost("/api/collections/{id}/items",
            async (
                int id,
                AddQuoteRequest request,
                ICollectionRepository repository,
                CancellationToken cancellationToken) =>
            {
                var collection = await repository.GetById(
                    id,
                    cancellationToken);

                if (collection == null)
                    return Results.NotFound();

                try
                {
                    // Aggregate controls the invariant
                    collection.AddItem(request.QuoteId);

                    await repository.Update(
                        collection,
                        cancellationToken);

                    return Results.Ok(collection);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(ex.Message);
                }
            });

        // Remove quote from collection
        app.MapDelete("/api/collections/{id}/items/{quoteId}",
            async (
                int id,
                int quoteId,
                ICollectionRepository repository,
                CancellationToken cancellationToken) =>
            {
                var collection = await repository.GetById(
                    id,
                    cancellationToken);

                if (collection == null)
                    return Results.NotFound();

                try
                {
                    // Aggregate controls the removal
                    collection.RemoveItem(quoteId);

                    await repository.Update(
                        collection,
                        cancellationToken);

                    return Results.NoContent();
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(ex.Message);
                }
            });
    }

    public record CreateCollectionRequest(
        string Name,
        int OwnerId);

    public record AddQuoteRequest(
        int QuoteId);
}