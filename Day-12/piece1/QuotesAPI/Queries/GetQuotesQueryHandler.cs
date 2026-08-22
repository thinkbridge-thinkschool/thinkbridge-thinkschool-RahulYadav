using MediatR;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Queries;

public class GetQuotesQueryHandler
    : IRequestHandler<GetQuotesQuery, List<QuoteReadModel>>
{
    private readonly QuotesDbContext _db;

    public GetQuotesQueryHandler(
        QuotesDbContext db)
    {
        _db = db;
    }

    public async Task<List<QuoteReadModel>> Handle(
        GetQuotesQuery request,
        CancellationToken cancellationToken)
    {
        return await _db.Quotes
            .AsNoTracking()
            .OrderBy(q => q.Id)
            .Skip((request.Page - 1) * request.Size)
            .Take(request.Size)
            .Select(q => new QuoteReadModel
            {
                Id = q.Id,
                Author = q.Author,
                Text = q.Text
            })
            .ToListAsync(cancellationToken);
    }
}