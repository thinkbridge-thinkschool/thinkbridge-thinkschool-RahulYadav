using MediatR;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Commands;

public class CreateQuoteCommandHandler
    : IRequestHandler<CreateQuoteCommand, int>
{
    private readonly QuotesDbContext _db;

    public CreateQuoteCommandHandler(
        QuotesDbContext db)
    {
        _db = db;
    }

    public async Task<int> Handle(
        CreateQuoteCommand request,
        CancellationToken cancellationToken)
    {
        // Validate the write request.
        if (string.IsNullOrWhiteSpace(request.Author))
        {
            throw new ArgumentException(
                "Author is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new ArgumentException(
                "Quote text is required.");
        }

        // Use the existing domain factory.
        var creation = Quote.Create(
            request.Author.Trim(),
            request.Text.Trim());

        if (!creation.IsSuccess ||
            creation.Quote is null)
        {
            throw new ArgumentException(
                creation.Error ?? "Unable to create quote.");
        }

        // Persist the normalized entity.
        _db.Quotes.Add(creation.Quote);

        await _db.SaveChangesAsync(
            cancellationToken);

        return creation.Quote.Id;
    }
}