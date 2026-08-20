using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// EF Core + SQLite
// ============================================================

builder.Services.AddDbContext<QuotesDbContext>(options =>
{
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection"));

    // Development-only SQL logging.
    options.LogTo(Console.WriteLine);

    // Development-only sensitive data logging.
    options.EnableSensitiveDataLogging();
});

var app = builder.Build();

// ============================================================
// Database
// ============================================================

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<QuotesDbContext>();

    db.Database.Migrate();
}

// ============================================================
// Day 10 Piece 2
// Full Entity vs DTO Projection
// ============================================================

app.MapGet(
    "/test-sql-projection",
    async (QuotesDbContext db) =>
    {
        // ----------------------------------------------------
        // 1. FULL ENTITY QUERY
        // ----------------------------------------------------
        // Loads the complete Quote entity.

        var fullEntityQuotes = await db.Quotes
            .Where(q => !q.IsDeleted)
            .OrderBy(q => q.Id)
            .Take(10)
            .ToListAsync();

        // ----------------------------------------------------
        // 2. DTO PROJECTION
        // ----------------------------------------------------
        // Only the required columns are selected.

        var projectedQuotes = await db.Quotes
            .Where(q => !q.IsDeleted)
            .OrderBy(q => q.Id)
            .Select(q => new QuoteDto
            {
                Id = q.Id,
                Author = q.Author,
                Text = q.Text
            })
            .Take(10)
            .ToListAsync();

        return Results.Ok(new
        {
            fullEntity = new
            {
                rows = fullEntityQuotes.Count
            },

            projection = new
            {
                rows = projectedQuotes.Count
            }
        });
    });

// ============================================================
// Day 10 Piece 2
// Accidental Client Evaluation
// ============================================================

app.MapGet(
    "/test-client-evaluation",
    async (QuotesDbContext db) =>
    {
        try
        {
            // Intentionally uses a custom C# method.
            // EF Core cannot translate this method into SQL.

            var quotes = await db.Quotes
                .Where(q => QuoteQueryHelpers.IsLongAuthor(q.Author))
                .ToListAsync();

            return Results.Ok(new
            {
                message = "Query executed.",
                rows = quotes.Count
            });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Ok(new
            {
                problem =
                    "The custom C# method could not be translated to SQL.",

                exception = ex.Message,

                fix =
                    "Use a SQL-translatable expression instead."
            });
        }
    });

// ============================================================
// Day 10 Piece 2
// Fixed Client Evaluation
// ============================================================

app.MapGet(
    "/test-client-evaluation-fixed",
    async (QuotesDbContext db) =>
    {
        // This expression can be translated to SQL.

        var quotes = await db.Quotes
            .Where(q => q.Author.Length > 10)
            .ToListAsync();

        return Results.Ok(new
        {
            message =
                "Query successfully translated to SQL.",

            rows = quotes.Count
        });
    });

app.Run();

// ============================================================
// Helper used for client-evaluation demonstration
// ============================================================

public static class QuoteQueryHelpers
{
    public static bool IsLongAuthor(string author)
    {
        return author.Length > 10;
    }
}