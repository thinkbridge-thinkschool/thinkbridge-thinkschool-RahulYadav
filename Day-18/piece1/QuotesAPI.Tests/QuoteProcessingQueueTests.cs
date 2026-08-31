using QuotesApi.BackgroundProcessing;

namespace QuotesApi.Tests;

// Exercises the queue's real producer/consumer behavior (System.Threading.
// Channels underneath), not just its source shape.
public sealed class QuoteProcessingQueueTests
{
    [Fact]
    public async Task QueueQuoteForProcessingAsync_ThenDequeueAllAsync_YieldsQueuedQuoteId()
    {
        var queue = new QuoteProcessingQueue();
        using var cts = new CancellationTokenSource();

        await queue.QueueQuoteForProcessingAsync(42, cts.Token);

        var reader = queue.DequeueAllAsync(cts.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(42, reader.Current);
    }

    [Fact]
    public async Task DequeueAllAsync_YieldsItemsInEnqueueOrder()
    {
        var queue = new QuoteProcessingQueue();
        using var cts = new CancellationTokenSource();

        await queue.QueueQuoteForProcessingAsync(1, cts.Token);
        await queue.QueueQuoteForProcessingAsync(2, cts.Token);
        await queue.QueueQuoteForProcessingAsync(3, cts.Token);

        var reader = queue.DequeueAllAsync(cts.Token).GetAsyncEnumerator();

        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(1, reader.Current);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(2, reader.Current);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(3, reader.Current);
    }

    [Fact]
    public async Task DequeueAllAsync_StopsCleanly_WhenCancellationRequestedOnEmptyQueue()
    {
        var queue = new QuoteProcessingQueue();
        using var cts = new CancellationTokenSource();

        var reader = queue.DequeueAllAsync(cts.Token).GetAsyncEnumerator();
        var moveNextTask = reader.MoveNextAsync();

        // Nothing queued — the enumerator is genuinely awaiting, not spinning.
        Assert.False(moveNextTask.IsCompleted);

        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await moveNextTask);
    }
}
