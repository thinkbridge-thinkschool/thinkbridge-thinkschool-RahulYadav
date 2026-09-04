namespace QuotesApi.Modules.Quotes.Contracts;

// The only shape of a Quote any other module is allowed to see. Collections
// depends on this, never on QuotesApi.Models.Quote or IQuoteRepository.
public sealed record QuoteSummary(int Id, string Author, string Text);
