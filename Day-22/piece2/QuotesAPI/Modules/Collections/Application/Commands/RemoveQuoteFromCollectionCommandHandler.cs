using QuotesApi.Modules.Collections.Application.Exceptions;
using QuotesApi.Modules.Collections.Application.Ports;
using QuotesApi.Modules.Collections.Contracts.Events;
using QuotesApi.Modules.Collections.Domain.Events;
using QuotesApi.Services;
using QuotesApi.Shared.Messaging;

namespace QuotesApi.Modules.Collections.Application.Commands;

// FLOW 3 — QuoteRemovedFromCollection:
// HTTP DELETE -> this handler -> Collection.RemoveQuote -> persist ->
// publish QuoteRemovedFromCollection -> Notifications consumes it
// asynchronously.
public sealed class RemoveQuoteFromCollectionCommandHandler
{
    private readonly ICollectionRepository _repository;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IClock _clock;

    public RemoveQuoteFromCollectionCommandHandler(
        ICollectionRepository repository,
        IIntegrationEventPublisher publisher,
        IClock clock)
    {
        _repository = repository;
        _publisher = publisher;
        _clock = clock;
    }

    public async Task HandleAsync(
        RemoveQuoteFromCollectionCommand command,
        CancellationToken cancellationToken)
    {
        var collection = await _repository.GetByIdAsync(command.CollectionId, cancellationToken)
            ?? throw new CollectionNotFoundException(command.CollectionId);

        collection.RemoveQuote(command.QuoteId, _clock.UtcNow);

        await _repository.SaveAsync(collection, cancellationToken);

        var domainEvent = (QuoteRemovedFromCollectionDomainEvent)collection.DequeueDomainEvents().Single();

        await _publisher.PublishAsync(
            new QuoteRemovedFromCollection(
                domainEvent.CollectionId,
                domainEvent.QuoteId,
                domainEvent.RemovedAtUtc),
            cancellationToken);
    }
}
