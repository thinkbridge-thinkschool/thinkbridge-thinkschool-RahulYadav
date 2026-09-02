# Day 20 Piece 1 — Transactional Outbox

## Objective

Guarantee that a database write and a Service Bus publish can never diverge. Implement the Transactional Outbox pattern on top of the existing Day 19 QuotesApi: the quote row and an `OutboxMessage` row describing the event are written in one EF Core transaction; a separate background relay is the only thing that ever publishes an outbox row to Service Bus and the only thing that marks it sent — and only after publishing actually succeeds.

The required guarantee is **at-least-once delivery with an idempotent consumer**, not exactly-once delivery. The relay may publish the same `MessageId` more than once around a crash window; the Day 19 consumer-side idempotency store is what makes that safe.

## What Day 19 Already Had, and What Was Reused

Nothing about the Day 19 messaging stack was rewritten. Reused as-is:

- `QuoteCreatedEvent` — the event schema. The outbox `Payload` column is exactly this type, serialized; no second event schema was invented.
- `ServiceBusQuoteEventPublisher` / `IQuoteEventPublisher` / `NullQuoteEventPublisher` — the relay publishes through this same abstraction. It has no idea whether it's talking to real Service Bus or the no-op publisher used in `Testing`.
- `ServiceBusSubscriptionWorker`, `QuoteEventMessageHandler`, `ProcessedMessage` / `ProcessedMessageStore` — the consumer side and its idempotency store are completely untouched. This is what the crash-safety proof below leans on.
- `QuotesDbContext` / SQLite, the `IServiceScopeFactory`-per-work-item pattern (`QuoteProcessingBackgroundService`, `ServiceBusSubscriptionWorker`), the `Configure<TOptions>` idiom, and Serilog logging.

## The Problem This Prevents

The naive sequence —

```text
1. Save quote
2. Publish to Service Bus
3. Save outbox bookkeeping
```

— can still diverge: a crash between steps 1 and 2 loses the event forever even though the quote exists; a crash between 2 and 3 can double-publish *and* still isn't durable proof anything was sent. The outbox pattern collapses "the quote exists" and "the event that must eventually reach Service Bus exists" into a single atomic database fact, then publishes out of band from a durable, retryable record.

## Architecture

```text
HTTP request
    |
    v
EF Core transaction (QuoteRepository.AddAsync)
    |
    +--> Quote row
    |
    +--> OutboxMessage row (MessageId, EventType, Payload, CreatedAtUtc)
             |
             v
        COMMIT
             |
             v
  OutboxRelayBackgroundService (polls unsent rows)
             |
             v
     Publish via IQuoteEventPublisher (Service Bus topic)
             |
             v
     SentAtUtc = now   (only after a successful publish)
             |
             v
  ServiceBusSubscriptionWorker (Day 19, unchanged)
             |
             v
  ProcessedMessageStore idempotency check (Day 19, unchanged)
```

## What Was Added

### `OutboxMessage` (`Models/OutboxMessage.cs`)

```csharp
public sealed class OutboxMessage
{
    public int Id { get; set; }
    public string MessageId { get; set; }      // stable, unique — never regenerated on retry
    public string EventType { get; set; }      // "QuoteCreated" (QuoteCreatedEvent.EventType)
    public string Payload { get; set; }        // serialized QuoteCreatedEvent
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? SentAtUtc { get; set; }   // null until a publish actually succeeds
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
}
```

`MessageId` (unique-indexed) is derived once, at insert time, from `QuoteCreatedEvent.BuildMessageId(quote.Id)` — the same function Day 19 already used for the Service Bus `MessageId`/idempotency key. It is never regenerated on retry, which is the whole reason a crashed-and-retried publish still lands on the consumer's existing dedup logic instead of a fresh, unrecognized id. EF migration: `Migrations/20260902075515_AddOutboxMessages.cs`.

### Atomic write — `QuoteRepository.AddAsync`

```csharp
public async Task<Quote> AddAsync(
    Quote quote,
    Func<Quote, OutboxMessage> buildOutboxMessage,
    CancellationToken cancellationToken)
{
    await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

    _context.Quotes.Add(quote);
    await _context.SaveChangesAsync(cancellationToken);   // assigns quote.Id, still inside the transaction

    _context.OutboxMessages.Add(buildOutboxMessage(quote));
    await _context.SaveChangesAsync(cancellationToken);

    await transaction.CommitAsync(cancellationToken);
    return quote;
}
```

The outbox row is built via a delegate rather than passed in pre-built, because the quote has no `Id` until SQLite assigns it — and that assignment has to happen inside the same transaction the outbox insert commits with, or the two could still be observed independently. `QuoteEndpointExtensions` (`POST /api/quotes`) no longer calls `IQuoteEventPublisher` directly at all; it only builds and hands over the `OutboxMessage`.

### `OutboxRelayBackgroundService`

Polls for unsent rows, publishes through the existing `IQuoteEventPublisher`, and only then marks the row sent:

```csharp
var quoteCreated = DeserializePayload(message);
var eventToPublish = quoteCreated with { MessageId = message.MessageId }; // outbox MessageId wins, always

await publisher.PublishQuoteCreatedAsync(eventToPublish, cancellationToken);

crashInjector.AfterPublishBeforeMarkSent(message);   // no-op in production — see below

message.AttemptCount++;
message.SentAtUtc = clock.UtcNow;
message.LastError = null;
await db.SaveChangesAsync(cancellationToken);
```

A publish failure keeps `SentAtUtc` null, records `LastError` and increments `AttemptCount`, and is retried on the next poll — the row is never dropped. Registered unconditionally in `Program.cs` (works the same whether the underlying publisher is real Service Bus or the `Testing`-environment no-op).

### Crash-injection seam — `IOutboxCrashInjector`

The one crash window this whole exercise exists to prove safe — publish succeeds, then the process dies before `SentAtUtc` is saved — can't be scripted with a real process kill reliably. `IOutboxCrashInjector.AfterPublishBeforeMarkSent` is called at exactly that point; production gets `NoOpOutboxCrashInjector` (does nothing), and a test injector throws `OutboxCrashSimulationException` on demand. The relay treats that exception specially: it persists **nothing** about the attempt — not `SentAtUtc`, not `AttemptCount`, not `LastError` — exactly as a real crash would leave it, then retries the row on the next poll under the same `MessageId`.

## Crash Safety — Proven, Not Just Claimed

`OutboxRelayBackgroundServiceTests.CrashAfterPublishBeforeMarkSent_RowSurvivesUnsent_IsRetried_AndConsumerDedupesTheDuplicate` runs this exact scenario against a real SQLite database with a real outbox row: seed a row → start the relay → it publishes and the simulated crash fires → stop the relay and inspect the row → restart a brand-new relay instance (a real process restart, not a continuation) → it republishes the same row under the same `MessageId` → succeeds → feed every delivery through the real `QuoteEventMessageHandler`/`ProcessedMessageStore` path. Actual captured output from that test run:

```text
[Initial state]  OutboxMessage MessageId=quote-1-created  SentAtUtc=NULL  AttemptCount=0
[After publish + simulated crash]  Service Bus received the message (1 publish(es) so far), but the process
'crashed' before the DB write: MessageId=quote-1-created  SentAtUtc=NULL  AttemptCount=0
(nothing about this attempt was persisted — the row was NOT lost).
[After relay restart + retry]  MessageId=quote-1-created  SentAtUtc=2026-09-02T08:18:21.9743829+00:00
AttemptCount=1  (published 2 time(s) total under this MessageId — at least once, nothing lost).
[Consumer idempotency]  2 delivery attempt(s) reached the consumer for MessageId=quote-1-created, but business
processing ran exactly 1 time — Day 19's ProcessedMessage store deduped the redelivery.
```

That is the pattern's guarantee made concrete: the message was published twice, nothing was ever lost, and the duplicate cost nothing because the consumer already knew how to ignore it.

## Tests

New test files, all against a real SQLite database (not fakes), plus the Day 19 idempotency store used as-is:

- `QuoteRepositoryOutboxTests` — the quote and outbox rows commit atomically; the outbox-message factory sees the quote's real, assigned `Id`.
- `QuoteCreatedEventSerializationTests` — the outbox payload round-trips deterministically; `BuildMessageId` is a pure function of the quote id.
- `OutboxRelayBackgroundServiceTests` — happy path (publish, mark sent), an ordinary publish failure (row stays unsent, error recorded, retried), and the crash-window scenario above end to end.

Two real bugs were caught and fixed while writing these:

1. **SQLite can't `ORDER BY` a `DateTimeOffset` column.** The relay's initial `OrderBy(x => x.CreatedAtUtc)` threw `NotSupportedException` on every poll (silently swallowed by the poll-level catch, which just made every test hang until timeout). Fixed to `OrderBy(x => x.Id)` — same effective ordering (insertion order), fully supported.
2. **A single shared `SqliteConnection` object isn't safe across threads.** `OutboxRelayBackgroundServiceTests` has genuine concurrency the earlier `ProcessedMessageStoreTests` pattern never needed — the relay's own background poll thread and the test's polling thread both touch the database at once — and sharing one `SqliteConnection` instance between them intermittently threw `SQLite Error 5: 'database is locked'` from inside EF's own connection setup. Fixed with the standard EF Core pattern for this: a uniquely-named shared-cache in-memory database (`mode=memory&cache=shared`), where every `DbContext` opens its own `SqliteConnection`. Verified stable across five repeated stress runs after the fix.

```text
33/34 passed
```

The one failure, `QuoteProcessingHttpTests.HostShutdown_DoesNotWaitOutInFlightSimulatedDelay`, is a pre-existing Day 18 test comparing two wall-clock shutdown durations against a hardcoded 6-second slack; it is unrelated to this feature, passed reliably in isolation (3/3 runs), and only flaked under full-parallel-suite CPU contention. `OutboxRelay:PollInterval` was set to 30 seconds in `appsettings.Testing.json` to reduce background polling chatter across the many `WebApplicationFactory`-based tests, though it did not fully eliminate that test's pre-existing timing sensitivity.

Backend build: **Passed**.

## What Would Break This

- **Horizontal scaling of the relay is not handled.** A single instance is assumed; running two relay instances concurrently would let both select the same unsent batch and publish it twice in parallel (still safe, thanks to the idempotent consumer, but wasteful). A real multi-instance deployment would need a row-claim mechanism (e.g. `UPDATE ... WHERE SentAtUtc IS NULL RETURNING` or a lease column).
- **Only `QuoteCreated` is a supported `EventType`.** `OutboxRelayBackgroundService.DeserializePayload` throws for anything else. Adding a second event type means extending that switch, not just inserting a row with a new `EventType` string.
- **No retention policy on `OutboxMessages`** — sent rows are never pruned, mirroring the same open caveat Day 19 already had for `ProcessedMessages`.
- **Losing `IOutboxCrashInjector`'s DI registration** would break `Program.cs` startup (`OutboxRelayBackgroundService` requires it); it must always resolve to `NoOpOutboxCrashInjector` in production.
- **Changing `QuoteCreatedEvent`'s shape** without a compatible relay/consumer deserialization path — no schema versioning is implemented, same caveat as Day 19.

## Final Status

- [x] `OutboxMessage` entity/table with `MessageId`, `EventType`, `Payload`, `CreatedAtUtc`, `SentAtUtc`, `AttemptCount`, `LastError`
- [x] Quote row + outbox row written in one explicit EF Core transaction
- [x] Outbox payload reuses `QuoteCreatedEvent` — no second event schema
- [x] `OutboxRelayBackgroundService`: never marks a row sent before a successful publish, records attempts/errors, retries, respects `CancellationToken`
- [x] Crash window simulated deterministically (`IOutboxCrashInjector`), not via random process kills
- [x] Proven, with a real outbox row: crash → row survives unsent → retried under the same `MessageId` → eventually sent → consumer dedupes the resulting duplicate
- [x] At-least-once delivery + idempotent consumer — exactly-once is explicitly not claimed
- [x] Day 19 messaging stack (publisher, subscription workers, idempotency store) untouched
- [x] No existing API endpoint contract changed
- [x] Backend tests: 33/34 passed (one pre-existing, unrelated, timing-sensitive Day 18 test)
- [x] Build passed
- [ ] Not committed — per instructions, no commit or push was made; branch remains `day19-piece1`
