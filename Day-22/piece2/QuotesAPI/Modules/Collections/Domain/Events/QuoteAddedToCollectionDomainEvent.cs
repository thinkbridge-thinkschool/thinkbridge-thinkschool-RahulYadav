namespace QuotesApi.Modules.Collections.Domain.Events;

public sealed record QuoteAddedToCollectionDomainEvent(
    int CollectionId,
    int QuoteId,
    DateTimeOffset AddedAtUtc) : IDomainEvent;
