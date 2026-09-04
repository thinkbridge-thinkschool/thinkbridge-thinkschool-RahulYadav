using QuotesApi.Shared.Messaging;

namespace QuotesApi.Modules.Collections.Contracts.Events;

// Published once a collection has been durably persisted. Notifications
// consumes this; nothing about how Collections stores a Collection leaks
// through it.
public sealed record CollectionCreated(
    int CollectionId,
    string Name,
    int OwnerId,
    DateTimeOffset CreatedAtUtc) : IIntegrationEvent;
