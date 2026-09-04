namespace QuotesApi.Modules.Collections.Contracts.Dtos;

// The only shape of a Collection ever returned across the HTTP boundary or
// to another module — never the EF-mapped Domain aggregate itself.
public sealed record CollectionDto(
    int Id,
    string Name,
    int OwnerId,
    IReadOnlyList<QuoteMembershipDto> Quotes);
