namespace QuotesApi.Models;

public class Collection
{
    private readonly List<CollectionItem> _items = new();

    public int Id { get; private set; }

    public string Name { get; private set; }

    public int OwnerId { get; private set; }

    public IReadOnlyCollection<CollectionItem> Items => _items.AsReadOnly();

    private Collection()
    {
        Name = string.Empty;
    }

    public Collection(string name, int ownerId)
    {
        ValidateName(name);

        if (ownerId <= 0)
            throw new ArgumentException("OwnerId must be greater than 0.");

        Name = name;
        OwnerId = ownerId;
    }

    public void AddItem(int quoteId)
    {
        if (quoteId <= 0)
            throw new ArgumentException("QuoteId must be greater than 0.");

        if (_items.Count >= 50)
            throw new InvalidOperationException(
                "A collection cannot contain more than 50 items.");

        if (_items.Any(x => x.QuoteId == quoteId))
            throw new InvalidOperationException(
                "This quote already exists in the collection.");

        _items.Add(new CollectionItem(quoteId));
    }

    public void RemoveItem(int quoteId)
    {
        var item = _items.FirstOrDefault(x => x.QuoteId == quoteId);

        if (item == null)
            throw new InvalidOperationException(
                "Quote does not exist in the collection.");

        _items.Remove(item);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Collection name is required.");

        if (name.Length < 3 || name.Length > 80)
            throw new ArgumentException(
                "Collection name must be between 3 and 80 characters.");
    }
}