using QuotesApi.Modules.Collections.Application.Exceptions;
using QuotesApi.Modules.Collections.Application.Mapping;
using QuotesApi.Modules.Collections.Application.Ports;
using QuotesApi.Modules.Collections.Contracts.Dtos;

namespace QuotesApi.Modules.Collections.Application.Queries;

public sealed class GetCollectionQueryHandler
{
    private readonly ICollectionRepository _repository;

    public GetCollectionQueryHandler(ICollectionRepository repository)
    {
        _repository = repository;
    }

    public async Task<CollectionDto> HandleAsync(
        GetCollectionQuery query,
        CancellationToken cancellationToken)
    {
        var collection = await _repository.GetByIdAsync(query.CollectionId, cancellationToken)
            ?? throw new CollectionNotFoundException(query.CollectionId);

        return CollectionMapper.ToDto(collection);
    }
}
