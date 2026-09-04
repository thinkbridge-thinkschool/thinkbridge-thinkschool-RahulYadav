namespace QuotesApi.Modules.Collections.Application.Exceptions;

// Raised when the Quotes module's IQuoteCatalog contract reports that a
// quote does not exist. Collections never inspects Quotes' own storage to
// find this out.
public sealed class QuoteNotFoundException : Exception
{
    public QuoteNotFoundException(int quoteId)
        : base($"Quote {quoteId} does not exist.")
    {
    }
}
