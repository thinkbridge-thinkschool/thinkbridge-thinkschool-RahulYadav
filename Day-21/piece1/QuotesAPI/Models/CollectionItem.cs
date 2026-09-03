namespace QuotesApi.Models;

public class CollectionItem
{
    public int QuoteId { get; private set; }

    public DateTime AddedAt { get; private set; }

    private CollectionItem()
    {
    }

    public CollectionItem(int quoteId)
    {
        if (quoteId <= 0)
            throw new ArgumentException("QuoteId must be greater than 0.");

        QuoteId = quoteId;
        AddedAt = DateTime.UtcNow;
    }
}