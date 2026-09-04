using QuotesApi.Modules.Collections.Application.Mapping;
using QuotesApi.Modules.Collections.Application.Ports;
using QuotesApi.Modules.Collections.Contracts.Dtos;
using QuotesApi.Modules.Collections.Contracts.Events;
using QuotesApi.Modules.Collections.Domain.Aggregates;
using QuotesApi.Modules.Collections.Domain.Events;
using QuotesApi.Services;
using QuotesApi.Shared.Messaging;

namespace QuotesApi.Modules.Collections.Application.Commands;

// FLOW 1 — CollectionCreated:
// HTTP POST -> this handler -> Collection.Create -> persist -> publish
// CollectionCreated -> Notifications consumes it asynchronously.
public sealed class CreateCollectionCommandHandler
{
    private readonly ICollectionRepository _repository;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IClock _clock;

    public CreateCollectionCommandHandler(
        ICollectionRepository repository,
        IIntegrationEventPublisher publisher,
        IClock clock)
    {
        _repository = repository;
        _publisher = publisher;
        _clock = clock;
    }

    public async Task<CollectionDto> HandleAsync(
        CreateCollectionCommand command,
        CancellationToken cancellationToken)
    {
        var collection = Collection.Create(command.Name, command.OwnerId);

        await _repository.AddAsync(collection, cancellationToken);

        collection.MarkCreated(_clock.UtcNow);

        var domainEvent = (CollectionCreatedDomainEvent)collection.DequeueDomainEvents().Single();

        await _publisher.PublishAsync(
            new CollectionCreated(
                domainEvent.CollectionId,
                domainEvent.Name,
                domainEvent.OwnerId,
                domainEvent.CreatedAtUtc),
            cancellationToken);

        return CollectionMapper.ToDto(collection);
    }
}
