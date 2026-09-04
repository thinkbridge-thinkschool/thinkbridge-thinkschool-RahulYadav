namespace QuotesApi.Modules.Collections.Application.Commands;

public sealed record RemoveQuoteFromCollectionCommand(int CollectionId, int QuoteId);
