using MediatR;

namespace QuotesApi.Commands;

public record CreateQuoteCommand(
    string Author,
    string Text
) : IRequest<int>;