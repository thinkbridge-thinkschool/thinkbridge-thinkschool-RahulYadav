using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Extensions;

public static class PerformanceEndpointExtensions
{
    public static void MapPerformanceEndpoints(this WebApplication app)
    {
        // ============================================================
        // Optimized endpoint
        // N+1 problem fixed by using a single EF Core query.
        // The Author column is still intentionally unindexed
        // so we can measure the effect of the missing index separately.
        // ============================================================

        app.MapGet("/api/performance/author-quotes", async (
            QuotesDbContext db,
            CancellationToken cancellationToken) =>
        {
            var result = await db.Quotes
                .Where(q => !q.IsDeleted)
                .GroupBy(q => q.Author)
                .Select(g => new
                {
                    Author = g.Key,
                    Quotes = g.Select(q => new
                    {
                        q.Id,
                        q.Author,
                        q.Text
                    }).ToList()
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(result);
        });

        // ============================================================
        // Temporary execution-plan endpoint
        // Used to inspect the SQLite query plan.
        // ============================================================

        app.MapGet("/api/performance/query-plan", async (
            QuotesDbContext db,
            CancellationToken cancellationToken) =>
        {
            var connection = db.Database.GetDbConnection();

            await connection.OpenAsync(cancellationToken);

            await using var command =
                connection.CreateCommand();

            command.CommandText = """
                EXPLAIN QUERY PLAN
                SELECT "q"."Id", "q"."Author", "q"."Text"
                FROM "Quotes" AS "q"
                WHERE NOT ("q"."IsDeleted")
                  AND "q"."Author" = 'Performance Author 1';
                """;

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            var plan = new List<object>();

            while (await reader.ReadAsync(
                cancellationToken))
            {
                plan.Add(new
                {
                    Id = reader.GetInt32(0),
                    Parent = reader.GetInt32(1),
                    NotUsed = reader.GetInt32(2),
                    Detail = reader.GetString(3)
                });
            }

            return Results.Ok(plan);
        });
    }
}