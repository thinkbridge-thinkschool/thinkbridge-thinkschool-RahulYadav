namespace QuotesApi.Modules.Collections.Application.Exceptions;

public sealed class CollectionNotFoundException : Exception
{
    public CollectionNotFoundException(int collectionId)
        : base($"Collection {collectionId} was not found.")
    {
    }
}
