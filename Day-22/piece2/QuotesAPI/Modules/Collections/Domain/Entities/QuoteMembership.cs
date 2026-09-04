namespace QuotesApi.Modules.Collections.Domain.Entities;

// Child entity of the Collection aggregate — a quote's membership in one
// collection. Never constructed or mutated outside Collection itself.
public sealed class QuoteMembership
{
    public int QuoteId { get; private set; }

    public DateTimeOffset AddedAtUtc { get; private set; }

    // EF Core materialization only.
    private QuoteMembership()
    {
    }

    internal QuoteMembership(int quoteId, DateTimeOffset addedAtUtc)
    {
        if (quoteId <= 0)
            throw new ArgumentException("QuoteId must be greater than 0.", nameof(quoteId));

        QuoteId = quoteId;
        AddedAtUtc = addedAtUtc;
    }
}
