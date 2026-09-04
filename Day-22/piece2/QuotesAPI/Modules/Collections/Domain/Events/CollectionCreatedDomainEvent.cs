namespace QuotesApi.Modules.Collections.Domain.Events;

public sealed record CollectionCreatedDomainEvent(
    int CollectionId,
    string Name,
    int OwnerId,
    DateTimeOffset CreatedAtUtc) : IDomainEvent;
