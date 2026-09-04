using QuotesApi.Shared.Messaging;

namespace QuotesApi.Modules.Collections.Contracts.Events;

public sealed record QuoteAddedToCollection(
    int CollectionId,
    int QuoteId,
    DateTimeOffset AddedAtUtc) : IIntegrationEvent;
