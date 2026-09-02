namespace QuotesApi.Messaging;

// Thrown only by a test IOutboxCrashInjector, never by production code.
// OutboxRelayBackgroundService treats this specific exception type
// differently from a genuine publish failure: it does NOT record an
// attempt, an error, or anything else about the row — a real process crash
// at this point would not get the chance to write anything either. See
// OutboxRelayBackgroundService.ProcessMessageAsync.
public sealed class OutboxCrashSimulationException : Exception
{
    public OutboxCrashSimulationException(string message) : base(message)
    {
    }
}
