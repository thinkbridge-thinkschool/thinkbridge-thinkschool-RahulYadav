using QuotesApi.Models;

namespace QuotesApi.Messaging;

// Production default: never crashes. Registered in Program.cs; tests
// substitute a different IOutboxCrashInjector to exercise the crash window
// deterministically (see IOutboxCrashInjector).
public sealed class NoOpOutboxCrashInjector : IOutboxCrashInjector
{
    public void AfterPublishBeforeMarkSent(OutboxMessage message)
    {
    }
}
