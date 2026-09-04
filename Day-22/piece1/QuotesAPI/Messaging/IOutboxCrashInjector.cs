using QuotesApi.Models;

namespace QuotesApi.Messaging;

// Deterministic crash-simulation seam for OutboxRelayBackgroundService.
//
// The one crash window this whole exercise exists to prove safe is: the
// relay successfully publishes an outbox row to Service Bus, then the
// process dies before it can persist SentAtUtc. A real process kill can't
// be scripted reliably in a test, so the relay calls this hook at exactly
// that point instead — right after a successful publish, right before the
// SentAtUtc/AttemptCount update is saved. The production implementation
// (NoOpOutboxCrashInjector) does nothing; a test implementation can throw
// OutboxCrashSimulationException on demand to make the relay behave exactly
// as if the process had crashed there (see
// OutboxRelayBackgroundService.ProcessMessageAsync).
public interface IOutboxCrashInjector
{
    void AfterPublishBeforeMarkSent(OutboxMessage message);
}
