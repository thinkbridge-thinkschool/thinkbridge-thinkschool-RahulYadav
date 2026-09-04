using QuotesApi.Modules.Quotes.Contracts;
using QuotesApi.Repositories;

namespace QuotesApi.Modules.Quotes.Application;

// Thin adapter over the existing (pre-Day-22-Piece-2) Quote persistence.
// The Quotes bounded context's implementation predates this module
// restructuring and already has extensive test/resilience/caching coverage
// (see QuotesAPI/Repositories, Caching, Resilience, Messaging), so it is
// deliberately left in place rather than moved wholesale under
// Modules/Quotes — this class is what turns it into a proper module
// boundary: everything outside Quotes now depends on IQuoteCatalog only.
internal sealed class QuoteCatalog : IQuoteCatalog
{
    private readonly IQuoteRepository _quoteRepository;

    public QuoteCatalog(IQuoteRepository quoteRepository)
    {
        _quoteRepository = quoteRepository;
    }

    public async Task<QuoteSummary?> FindAsync(int quoteId, CancellationToken cancellationToken)
    {
        var quote = await _quoteRepository.GetByIdAsync(quoteId, cancellationToken);

        return quote is null
            ? null
            : new QuoteSummary(quote.Id, quote.Author, quote.Text);
    }
}
