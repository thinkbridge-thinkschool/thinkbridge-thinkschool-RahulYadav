using MediatR;

namespace QuotesApi.Queries;

public record GetQuotesDapperQuery(
    int Page,
    int Size
) : IRequest<List<QuoteReadModel>>;