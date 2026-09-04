using System.Threading.Channels;

namespace QuotesApi.BackgroundProcessing;

// Bounded, in-process queue backed by System.Threading.Channels.
// Bounded (rather than unbounded) so a burst of quote creations can't grow
// memory without limit if the background worker falls behind; the writer
// asynchronously waits for space instead of blocking a thread.
//
// Registered as a singleton via DI (see Program.cs) — never constructed
// with `new` outside of DI/tests — so the same channel instance is shared
// between the HTTP request path (writer) and the BackgroundService
// (reader).
public sealed class QuoteProcessingQueue : IQuoteProcessingQueue
{
    private readonly Channel<int> _channel;

    public QuoteProcessingQueue()
    {
        var options = new BoundedChannelOptions(capacity: 100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };

        _channel = Channel.CreateBounded<int>(options);
    }

    public async ValueTask QueueQuoteForProcessingAsync(
        int quoteId,
        CancellationToken cancellationToken)
    {
        await _channel.Writer.WriteAsync(quoteId, cancellationToken);
    }

    public IAsyncEnumerable<int> DequeueAllAsync(
        CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
