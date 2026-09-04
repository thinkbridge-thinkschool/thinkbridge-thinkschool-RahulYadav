using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Modules.Collections.Application.Ports;
using QuotesApi.Modules.Collections.Domain.Aggregates;

namespace QuotesApi.Modules.Collections.Infrastructure.Repositories;

// Only this module is allowed to hold an ICollectionRepository/Collection
// dependency; it is the sole reader/writer of the Collections/CollectionItems
// tables in the shared QuotesDbContext.
internal sealed class EfCollectionRepository : ICollectionRepository
{
    private readonly QuotesDbContext _db;

    public EfCollectionRepository(QuotesDbContext db)
    {
        _db = db;
    }

    public async Task<Collection?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await _db.Collections
            .Include(x => x.QuoteMemberships)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task AddAsync(Collection collection, CancellationToken cancellationToken)
    {
        await _db.Collections.AddAsync(collection, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(Collection collection, CancellationToken cancellationToken)
    {
        _db.Collections.Update(collection);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
