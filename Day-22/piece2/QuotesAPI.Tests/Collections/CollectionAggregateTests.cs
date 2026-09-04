using QuotesApi.Modules.Collections.Domain.Aggregates;
using QuotesApi.Modules.Collections.Domain.Events;

namespace QuotesApi.Tests.Collections;

// Pure domain tests: no DbContext, no HTTP, no DI — proving the Collection
// aggregate enforces its own invariants regardless of how it is hosted.
public class CollectionAggregateTests
{
    [Fact]
    public void Create_WithValidNameAndOwner_Succeeds()
    {
        var collection = Collection.Create("Motivation", ownerId: 1);

        Assert.Equal("Motivation", collection.Name);
        Assert.Equal(1, collection.OwnerId);
        Assert.Empty(collection.QuoteMemberships);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    public void Create_WithInvalidName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => Collection.Create(name, ownerId: 1));
    }

    [Fact]
    public void Create_WithNonPositiveOwnerId_Throws()
    {
        Assert.Throws<ArgumentException>(() => Collection.Create("Motivation", ownerId: 0));
    }

    [Fact]
    public void MarkCreated_RaisesCollectionCreatedDomainEvent()
    {
        var collection = Collection.Create("Motivation", ownerId: 1);
        var now = DateTimeOffset.UtcNow;

        collection.MarkCreated(now);

        var domainEvent = Assert.Single(collection.DomainEvents);
        var created = Assert.IsType<CollectionCreatedDomainEvent>(domainEvent);
        Assert.Equal("Motivation", created.Name);
        Assert.Equal(1, created.OwnerId);
        Assert.Equal(now, created.CreatedAtUtc);
    }

    [Fact]
    public void AddQuote_NewQuote_AddsMembershipAndRaisesDomainEvent()
    {
        var collection = Collection.Create("Motivation", ownerId: 1);
        var now = DateTimeOffset.UtcNow;

        collection.AddQuote(12, now);

        var membership = Assert.Single(collection.QuoteMemberships);
        Assert.Equal(12, membership.QuoteId);
        Assert.Equal(now, membership.AddedAtUtc);

        var domainEvent = Assert.Single(collection.DomainEvents);
        var added = Assert.IsType<QuoteAddedToCollectionDomainEvent>(domainEvent);
        Assert.Equal(12, added.QuoteId);
    }

    [Fact]
    public void AddQuote_DuplicateQuote_Throws()
    {
        var collection = Collection.Create("Motivation", ownerId: 1);
        collection.AddQuote(12, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(
            () => collection.AddQuote(12, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void AddQuote_BeyondMaxQuoteMemberships_Throws()
    {
        var collection = Collection.Create("Motivation", ownerId: 1);

        for (var quoteId = 1; quoteId <= Collection.MaxQuoteMemberships; quoteId++)
        {
            collection.AddQuote(quoteId, DateTimeOffset.UtcNow);
        }

        Assert.Throws<InvalidOperationException>(
            () => collection.AddQuote(Collection.MaxQuoteMemberships + 1, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RemoveQuote_ExistingQuote_RemovesMembershipAndRaisesDomainEvent()
    {
        var collection = Collection.Create("Motivation", ownerId: 1);
        collection.AddQuote(12, DateTimeOffset.UtcNow);
        collection.DequeueDomainEvents();

        var now = DateTimeOffset.UtcNow;
        collection.RemoveQuote(12, now);

        Assert.Empty(collection.QuoteMemberships);

        var domainEvent = Assert.Single(collection.DomainEvents);
        var removed = Assert.IsType<QuoteRemovedFromCollectionDomainEvent>(domainEvent);
        Assert.Equal(12, removed.QuoteId);
        Assert.Equal(now, removed.RemovedAtUtc);
    }

    [Fact]
    public void RemoveQuote_QuoteNotInCollection_Throws()
    {
        var collection = Collection.Create("Motivation", ownerId: 1);

        Assert.Throws<InvalidOperationException>(
            () => collection.RemoveQuote(999, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void DequeueDomainEvents_ClearsPendingEvents()
    {
        var collection = Collection.Create("Motivation", ownerId: 1);
        collection.AddQuote(12, DateTimeOffset.UtcNow);

        var first = collection.DequeueDomainEvents();
        var second = collection.DequeueDomainEvents();

        Assert.Single(first);
        Assert.Empty(second);
        Assert.Empty(collection.DomainEvents);
    }

    [Fact]
    public void Rename_WithValidName_UpdatesName()
    {
        var collection = Collection.Create("Motivation", ownerId: 1);

        collection.Rename("Wisdom");

        Assert.Equal("Wisdom", collection.Name);
    }

    [Fact]
    public void Rename_WithInvalidName_Throws()
    {
        var collection = Collection.Create("Motivation", ownerId: 1);

        Assert.Throws<ArgumentException>(() => collection.Rename(""));
    }
}
