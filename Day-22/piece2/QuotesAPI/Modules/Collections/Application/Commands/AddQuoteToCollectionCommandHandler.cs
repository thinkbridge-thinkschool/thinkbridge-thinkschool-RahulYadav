using QuotesApi.Modules.Collections.Application.Exceptions;
using QuotesApi.Modules.Collections.Application.Mapping;
using QuotesApi.Modules.Collections.Application.Ports;
using QuotesApi.Modules.Collections.Contracts.Dtos;
using QuotesApi.Modules.Collections.Contracts.Events;
using QuotesApi.Modules.Collections.Domain.Events;
using QuotesApi.Modules.Quotes.Contracts;
using QuotesApi.Services;
using QuotesApi.Shared.Messaging;

namespace QuotesApi.Modules.Collections.Application.Commands;

// FLOW 2 — QuoteAddedToCollection:
// HTTP POST -> this handler -> IQuoteCatalog (Quotes' public contract, a
// synchronous cross-module call) -> Collection.AddQuote -> persist ->
// publish QuoteAddedToCollection -> Notifications consumes it asynchronously.
public sealed class AddQuoteToCollectionCommandHandler
{
    private readonly ICollectionRepository _repository;
    private readonly IQuoteCatalog _quoteCatalog;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IClock _clock;

    public AddQuoteToCollectionCommandHandler(
        ICollectionRepository repository,
        IQuoteCatalog quoteCatalog,
        IIntegrationEventPublisher publisher,
        IClock clock)
    {
        _repository = repository;
        _quoteCatalog = quoteCatalog;
        _publisher = publisher;
        _clock = clock;
    }

    public async Task<CollectionDto> HandleAsync(
        AddQuoteToCollectionCommand command,
        CancellationToken cancellationToken)
    {
        var collection = await _repository.GetByIdAsync(command.CollectionId, cancellationToken)
            ?? throw new CollectionNotFoundException(command.CollectionId);

        var quote = await _quoteCatalog.FindAsync(command.QuoteId, cancellationToken)
            ?? throw new QuoteNotFoundException(command.QuoteId);

        collection.AddQuote(quote.Id, _clock.UtcNow);

        await _repository.SaveAsync(collection, cancellationToken);

        var domainEvent = (QuoteAddedToCollectionDomainEvent)collection.DequeueDomainEvents().Single();

        await _publisher.PublishAsync(
            new QuoteAddedToCollection(
                domainEvent.CollectionId,
                domainEvent.QuoteId,
                domainEvent.AddedAtUtc),
            cancellationToken);

        return CollectionMapper.ToDto(collection);
    }
}
