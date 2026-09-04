namespace QuotesApi.Modules.Collections.Domain.Events;

public sealed record QuoteRemovedFromCollectionDomainEvent(
    int CollectionId,
    int QuoteId,
    DateTimeOffset RemovedAtUtc) : IDomainEvent;
