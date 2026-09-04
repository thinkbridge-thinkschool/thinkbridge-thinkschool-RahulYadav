using QuotesApi.Messaging;
using QuotesApi.Models;

namespace QuotesApi.Tests;

// Deterministic stand-in for a process crash (see IOutboxCrashInjector).
// A test schedules a crash for a specific outbox row via
// CrashOnNextPublish(messageId); the NEXT time
// OutboxRelayBackgroundService reaches AfterPublishBeforeMarkSent for that
// exact MessageId, this throws OutboxCrashSimulationException instead of
// letting the row be marked sent — then clears the schedule, so the
// relay's retry on the same row is allowed through normally.
internal sealed class TestOutboxCrashInjector : IOutboxCrashInjector
{
    private readonly HashSet<string> _crashOnceForMessageIds = new();

    public void CrashOnNextPublish(string messageId)
    {
        lock (_crashOnceForMessageIds)
        {
            _crashOnceForMessageIds.Add(messageId);
        }
    }

    public void AfterPublishBeforeMarkSent(OutboxMessage message)
    {
        bool shouldCrash;

        lock (_crashOnceForMessageIds)
        {
            shouldCrash = _crashOnceForMessageIds.Remove(message.MessageId);
        }

        if (shouldCrash)
        {
            throw new OutboxCrashSimulationException(
                $"Simulated crash after publishing {message.MessageId}, " +
                "before SentAtUtc was persisted.");
        }
    }
}
