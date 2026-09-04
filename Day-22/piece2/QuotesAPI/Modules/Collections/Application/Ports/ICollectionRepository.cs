using QuotesApi.Modules.Collections.Domain.Aggregates;

namespace QuotesApi.Modules.Collections.Application.Ports;

// Owned by the Collections module's Application layer; implemented by
// Infrastructure. No other module may depend on this interface or on
// Collection itself.
public interface ICollectionRepository
{
    Task<Collection?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task AddAsync(Collection collection, CancellationToken cancellationToken);

    Task SaveAsync(Collection collection, CancellationToken cancellationToken);
}
