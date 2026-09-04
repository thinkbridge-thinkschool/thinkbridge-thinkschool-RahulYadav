using QuotesApi.Modules.Collections.Domain.Entities;
using QuotesApi.Modules.Collections.Domain.Events;

namespace QuotesApi.Modules.Collections.Domain.Aggregates;

// Aggregate root and consistency boundary for a named group of quotes owned
// by a user. Every invariant about a collection's membership is enforced
// here and nowhere else:
//   - a collection's name is never empty and stays within length limits
//   - a quote can belong to a collection at most once
//   - a collection cannot grow past MaxQuoteMemberships
// Application code (command handlers) may only reach QuoteMemberships
// through AddQuote/RemoveQuote/Rename — there is no public mutator that
// bypasses these rules.
public sealed class Collection
{
    public const int MaxQuoteMemberships = 50;

    private readonly List<QuoteMembership> _quoteMemberships = new();
    private readonly List<IDomainEvent> _domainEvents = new();

    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int OwnerId { get; private set; }

    public IReadOnlyCollection<QuoteMembership> QuoteMemberships => _quoteMemberships.AsReadOnly();

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    // EF Core materialization only; application code must go through Create.
    private Collection()
    {
    }

    public static Collection Create(string name, int ownerId)
    {
        ValidateName(name);

        if (ownerId <= 0)
            throw new ArgumentException("OwnerId must be greater than 0.", nameof(ownerId));

        return new Collection
        {
            Name = name,
            OwnerId = ownerId
        };
    }

    // Id is a database-assigned autoincrement value, only known once the
    // repository has inserted the aggregate — so the CollectionCreated
    // domain event cannot be raised inside Create itself. The application
    // handler calls this immediately after persisting, once Id is populated.
    public void MarkCreated(DateTimeOffset createdAtUtc) =>
        _domainEvents.Add(new CollectionCreatedDomainEvent(Id, Name, OwnerId, createdAtUtc));

    public void Rename(string name)
    {
        ValidateName(name);

        Name = name;
    }

    public void AddQuote(int quoteId, DateTimeOffset addedAtUtc)
    {
        if (quoteId <= 0)
            throw new ArgumentException("QuoteId must be greater than 0.", nameof(quoteId));

        if (_quoteMemberships.Count >= MaxQuoteMemberships)
            throw new InvalidOperationException(
                $"A collection cannot contain more than {MaxQuoteMemberships} quotes.");

        if (_quoteMemberships.Any(x => x.QuoteId == quoteId))
            throw new InvalidOperationException(
                "This quote already exists in the collection.");

        _quoteMemberships.Add(new QuoteMembership(quoteId, addedAtUtc));

        _domainEvents.Add(new QuoteAddedToCollectionDomainEvent(Id, quoteId, addedAtUtc));
    }

    public void RemoveQuote(int quoteId, DateTimeOffset removedAtUtc)
    {
        var membership = _quoteMemberships.FirstOrDefault(x => x.QuoteId == quoteId);

        if (membership is null)
            throw new InvalidOperationException("Quote does not exist in the collection.");

        _quoteMemberships.Remove(membership);

        _domainEvents.Add(new QuoteRemovedFromCollectionDomainEvent(Id, quoteId, removedAtUtc));
    }

    public IReadOnlyCollection<IDomainEvent> DequeueDomainEvents()
    {
        var events = _domainEvents.ToList();
        _domainEvents.Clear();
        return events;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Collection name is required.", nameof(name));

        if (name.Length < 3 || name.Length > 80)
            throw new ArgumentException(
                "Collection name must be between 3 and 80 characters.", nameof(name));
    }
}
