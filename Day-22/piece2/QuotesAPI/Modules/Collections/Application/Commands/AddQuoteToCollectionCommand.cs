namespace QuotesApi.Modules.Collections.Application.Commands;

public sealed record AddQuoteToCollectionCommand(int CollectionId, int QuoteId);
