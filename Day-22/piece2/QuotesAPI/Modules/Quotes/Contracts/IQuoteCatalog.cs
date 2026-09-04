namespace QuotesApi.Modules.Quotes.Contracts;

// Public synchronous contract for the Quotes bounded context. Other modules
// (Collections) call this instead of reaching into QuotesApi.Repositories.
public interface IQuoteCatalog
{
    Task<QuoteSummary?> FindAsync(int quoteId, CancellationToken cancellationToken);
}
