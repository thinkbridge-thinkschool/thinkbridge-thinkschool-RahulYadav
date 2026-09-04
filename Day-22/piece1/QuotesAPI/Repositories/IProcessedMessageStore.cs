namespace QuotesApi.Repositories;

// Idempotency check used by ServiceBusSubscriptionWorker before it does
// any work for a delivered message, and to record success afterward. See
// Models/ProcessedMessage.cs for why this is keyed per subscription and
// backed by the database rather than in-memory state.
public interface IProcessedMessageStore
{
    Task<bool> HasBeenProcessedAsync(
        string subscriptionName,
        string messageId,
        CancellationToken cancellationToken);

    // Idempotent itself: marking the same (subscriptionName, messageId)
    // twice — e.g. a race between two competing consumers that both passed
    // the HasBeenProcessedAsync check before either finished — is a no-op
    // on the second call rather than an error.
    Task MarkProcessedAsync(
        string subscriptionName,
        string messageId,
        CancellationToken cancellationToken);
}
