using MediatR;

namespace QuotesApi.Queries;

public record GetQuotesQuery(
    int Page,
    int Size
) : IRequest<List<QuoteReadModel>>;