using Dapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Queries;

public class GetQuotesDapperQueryHandler
    : IRequestHandler<GetQuotesDapperQuery, List<QuoteReadModel>>
{
    private readonly QuotesDbContext _db;

    public GetQuotesDapperQueryHandler(
        QuotesDbContext db)
    {
        _db = db;
    }

    public async Task<List<QuoteReadModel>> Handle(
        GetQuotesDapperQuery request,
        CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();

        const string sql = """
            SELECT
                Id,
                Author,
                Text
            FROM Quotes
            ORDER BY Id
            LIMIT @Size
            OFFSET @Offset;
            """;

        var offset =
            (request.Page - 1) * request.Size;

        var command = new CommandDefinition(
            sql,
            new
            {
                Size = request.Size,
                Offset = offset
            },
            cancellationToken: cancellationToken);

        var quotes = await connection.QueryAsync<QuoteReadModel>(
            command);

        return quotes.ToList();
    }
}