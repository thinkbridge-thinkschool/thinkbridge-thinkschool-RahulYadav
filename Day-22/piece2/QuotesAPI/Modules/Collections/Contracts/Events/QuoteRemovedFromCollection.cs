using QuotesApi.Shared.Messaging;

namespace QuotesApi.Modules.Collections.Contracts.Events;

public sealed record QuoteRemovedFromCollection(
    int CollectionId,
    int QuoteId,
    DateTimeOffset RemovedAtUtc) : IIntegrationEvent;
