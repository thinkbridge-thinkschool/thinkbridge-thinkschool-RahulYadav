namespace QuotesApi.BackgroundProcessing;

// Producer/consumer contract between the HTTP request path and the
// background worker. The request path only ever calls
// QueueQuoteForProcessingAsync — it never awaits the slow work itself.
public interface IQuoteProcessingQueue
{
    ValueTask QueueQuoteForProcessingAsync(
        int quoteId,
        CancellationToken cancellationToken);

    IAsyncEnumerable<int> DequeueAllAsync(
        CancellationToken cancellationToken);
}
