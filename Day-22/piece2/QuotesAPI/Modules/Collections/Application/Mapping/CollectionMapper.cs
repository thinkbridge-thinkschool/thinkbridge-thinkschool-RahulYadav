using QuotesApi.Modules.Collections.Contracts.Dtos;
using QuotesApi.Modules.Collections.Domain.Aggregates;

namespace QuotesApi.Modules.Collections.Application.Mapping;

internal static class CollectionMapper
{
    public static CollectionDto ToDto(Collection collection) => new(
        collection.Id,
        collection.Name,
        collection.OwnerId,
        collection.QuoteMemberships
            .Select(x => new QuoteMembershipDto(x.QuoteId, x.AddedAtUtc))
            .ToList());
}
