namespace QuotesApi.Models;

// Day 21: cache-safe projection of Quote returned by the HybridCache-backed
// read path (see Caching/QuoteCacheReader.cs). A plain record with a public
// constructor round-trips through System.Text.Json (and therefore through
// Redis as HybridCache's L2) cleanly; Quote itself cannot, since its only
// parameterless constructor is private and it exposes no public setters.
public sealed record CachedQuote(int Id, string Author, string Text, bool IsDeleted)
{
    public static CachedQuote FromQuote(Quote quote) =>
        new(quote.Id, quote.Author, quote.Text, quote.IsDeleted);
}
