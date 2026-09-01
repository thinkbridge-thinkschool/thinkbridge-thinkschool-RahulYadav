# Day 19 Piece 1 — Service Bus Topic, Subscriptions, Competing Consumers, Idempotency, DLQ

## Objective

Extend the Day 18 QuotesApi with a real Azure Service Bus topic/subscription pub-sub flow, on top of (not instead of) the existing Day 18 in-process queue + `BackgroundService`.

This exercise demonstrates: publishing to a topic, two independent subscriptions each receiving their own copy of a message, competing consumers within one subscription, idempotent handling keyed on the Service Bus `MessageId`, and a poison message being retried and dead-lettered by the real Service Bus DLQ.

## What Day 18 Already Had, and What Was Reused

`QuoteProcessingQueue` + `QuoteProcessingBackgroundService` (an in-process `Channel<int>` drained by a `BackgroundService`) were left completely unmodified — they still do this API's own background formatting work and still pass all their existing tests unchanged. Day 19 does not duplicate that pattern with a second local queue; the topic is an entirely separate, broker-backed fan-out mechanism.

Also reused as-is: the DI/scoping pattern (a new `IServiceScopeFactory` scope per work item, since a singleton worker cannot depend on scoped services directly), the `QuotesDbContext`/SQLite persistence, Serilog logging, and the `Configure<TOptions>` + conditional-registration idiom Program.cs already used for Key Vault and Azure Monitor (a config value being empty means "this feature is off").

## Architecture

```text
POST /api/quotes
      |
      +--> Day 18: local queue --> QuoteProcessingBackgroundService (unchanged)
      |
      +--> Day 19: publish QuoteCreated event to Service Bus topic
                          |
                          v
                 Topic: quote-events
                          |
              +-----------+-----------+
              |                       |
              v                       v
      Subscription: sub-a     Subscription: sub-b
              |                       |
      +-------+-------+               v
      v               v          Worker-B1
  Worker-A1       Worker-A2   (single consumer)
   (competing consumers on the same subscription)
```

**Subscriptions vs. competing consumers — the two mechanisms this exercise asks to keep distinct:**

- A **subscription** is an independent copy of every message published to the topic. `sub-a` and `sub-b` each got their own copy of every message in the demo below — that's fan-out to different consumer groups (e.g. one subscription could feed a search index, another could send notifications).
- **Competing consumers** are multiple readers pulling from the *same* subscription. `Worker-A1` and `Worker-A2` both attach a `ServiceBusProcessor` to `sub-a`; Service Bus hands each message to whichever one is free, and — as captured below — even redeliveries of the *same* message after an abandon can land on a *different* competing worker.

## Resources — Reused, Not Created

Per the instructions, the Azure subscription was inspected read-only before creating anything:

```text
az servicebus namespace list -o table
az servicebus topic list --namespace-name sb-day19-quotedemo --resource-group thinkschool-rg -o table
az servicebus topic subscription list --namespace-name sb-day19-quotedemo --resource-group thinkschool-rg --topic-name quote-events -o table
```

This found a namespace already provisioned for this exact exercise, so **nothing was created**:

| Resource | Name | Notes |
|---|---|---|
| Resource group | `thinkschool-rg` | existing |
| Namespace | `sb-day19-quotedemo` | Standard tier (required for topics), `eastasia` |
| Topic | `quote-events` | already existed |
| Subscription A | `sub-a` | `MaxDeliveryCount = 3`, `LockDuration = 30s` |
| Subscription B | `sub-b` | same settings, independent of `sub-a` |

`ServiceBusOptions` in code defaults to these exact names (`TopicName = "quote-events"`, `SubscriptionA = "sub-a"`, `SubscriptionB = "sub-b"`) rather than inventing new `day19-*` names that would have collided with or duplicated an already-correct setup. No existing resource was modified or deleted.

## Authentication — No Secret Anywhere

No connection string, SAS key, or client secret is configured or stored anywhere in this repository. The current Azure identity (`az account show`) already holds the **Azure Service Bus Data Owner** role directly on `sb-day19-quotedemo`:

```text
az role assignment list --all -o table
...
shubh.rastogi2@s.amity.edu  Azure Service Bus Data Owner  .../namespaces/sb-day19-quotedemo
```

`ServiceBusClient` is constructed with a `TokenCredential` chosen by environment (`Program.cs`):

```csharp
Azure.Core.TokenCredential credential =
    builder.Environment.IsDevelopment()
        ? new AzureCliCredential()
        : new DefaultAzureCredential();
```

`DefaultAzureCredential` is what production (Azure Container Apps) should use — it picks up a managed identity automatically with no code change once one is assigned. Locally there is no Instance Metadata Service to answer, and `DefaultAzureCredential`'s full probe chain (workload identity, then managed identity via IMDS with several retries) burns real wall-clock time — multiple minutes in this sandboxed dev machine — before it ever reaches the developer's own `az login` session. `AzureCliCredential` in `Development` skips straight to that already-authenticated session.

`ServiceBus:FullyQualifiedNamespace` (`sb-day19-quotedemo.servicebus.windows.net`) is a hostname, not a secret, so it lives in `appsettings.Development.json`. The base `appsettings.json` value is intentionally empty, so the `Testing` environment (used by `QuotesApiFactory`/integration tests) and any environment without it configured never attempt Azure connectivity — a `NullQuoteEventPublisher` is registered instead and no subscription workers start. Production would set the same key via an app setting / managed identity, not a committed file.

## Publisher

`ServiceBusQuoteEventPublisher` (`Messaging/ServiceBusQuoteEventPublisher.cs`), registered as a singleton wrapping one long-lived `ServiceBusSender` (created once, disposed by DI at shutdown via `IAsyncDisposable`):

```csharp
var message = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(quoteCreated))
{
    MessageId = quoteCreated.MessageId,   // stable per quote — see idempotency below
    ContentType = "application/json",
    Subject = "QuoteCreated",
};
await _sender.SendMessageAsync(message, cancellationToken);
```

It is called from the existing `POST /api/quotes` handler (`QuoteEndpointExtensions.cs`), immediately after the Day 18 local-queue enqueue — no new endpoint was added. The event is `QuoteCreatedEvent`, a small domain event based on the existing `Quote` model (author/text/quoteId), not an invented business concept.

## Consumers

`ServiceBusSubscriptionWorker` (`Messaging/ServiceBusSubscriptionWorker.cs`) is a `BackgroundService`; three instances are registered — `Worker-A1` and `Worker-A2` against `sub-a`, `Worker-B1` against `sub-b`. Each owns its own `ServiceBusProcessor` (`MaxConcurrentCalls = 1`, `AutoCompleteMessages = false`, so settlement is explicit):

```csharp
await handler.HandleAsync(_subscriptionName, _workerName, message.MessageId, message.Body, args.CancellationToken);
await args.CompleteMessageAsync(message, args.CancellationToken);
```
```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "... abandoning for retry.");
    await args.AbandonMessageAsync(message, cancellationToken: args.CancellationToken);
    // Service Bus itself dead-letters the message once the subscription's
    // MaxDeliveryCount (3) is exceeded — no application code touches the DLQ.
}
```

## Idempotency

`ProcessedMessage` (EF Core entity, composite key `(SubscriptionName, MessageId)`) persisted in the same `QuotesDbContext`/SQLite database Day 18 already uses. Keyed per subscription because `sub-a` and `sub-b` are independent copies and must each track their own "have I seen this MessageId" state; backed by the database (not in-process memory) because competing consumers are separate `BackgroundService` instances that must agree, and that state must survive a worker restart.

```csharp
public async Task HandleAsync(string subscriptionName, string workerName, string messageId, BinaryData body, CancellationToken ct)
{
    if (await _processedMessages.HasBeenProcessedAsync(subscriptionName, messageId, ct))
    {
        _logger.LogInformation("... already processed; skipping duplicate delivery.");
        return;
    }
    ...
    await _processedMessages.MarkProcessedAsync(subscriptionName, messageId, ct);
}
```

`MarkProcessedAsync` tolerates the database's unique-constraint violation instead of throwing, so two competing consumers that both pass the "not processed yet" check before either finishes don't crash or double-record — see `ProcessedMessageStoreTests.MarkProcessedAsync_TwoCompetingConsumersRaceOnSameMessage_SecondCallDoesNotThrow`, which models exactly that race with two separate `DbContext` instances against the same database.

## Poison Message → Retry → Real DLQ

No DLQ is faked in application memory. A processing failure only ever calls `AbandonMessageAsync`; Service Bus's own delivery-count tracking (subscription `MaxDeliveryCount = 3`) moves the message to the real dead-letter queue once exceeded.

Two independent poison scenarios were exercised end to end against the live namespace:

1. **Pre-existing seeded data** — the namespace already had a non-JSON poison message and normal quote messages sitting in `sub-a`/`sub-b` before this session touched them. Running the app processed them for real.
2. **This app's own poison marker** (`QuoteCreatedEvent.PoisonAuthorMarker`, author `__day19_poison_test__`) — created via the ordinary `POST /api/quotes` endpoint, no test-only endpoint added, to prove the same mechanism works for genuinely application-triggered failures.

Actual captured console output (`dotnet run`, `ASPNETCORE_ENVIRONMENT=Development`), condensed:

```text
Worker-A1 started, listening on subscription sub-a.
Worker-A2 started, listening on subscription sub-a.
Worker-B1 started, listening on subscription sub-b.

[Worker-B1/sub-b] Processing quote 1 by Albert Einstein (quote-...).
[Worker-B1/sub-b] Processing quote 2 by Marie Curie (quote-...).
[Worker-B1/sub-b] Processing quote 3 by Ada Lovelace (quote-...).
[Worker-B1/sub-b] quote-...(quote 2) already processed; skipping duplicate delivery.
[Worker-B1/sub-b] Failed to process poison-... (delivery attempt 1); abandoning for retry.
[Worker-B1/sub-b] Failed to process poison-... (delivery attempt 2); abandoning for retry.
[Worker-B1/sub-b] Failed to process poison-... (delivery attempt 3); abandoning for retry.

--- after publishing 4 new quotes + 1 poison quote via POST /api/quotes ---

[Worker-B1/sub-b] Processing quote 23 by Demo Author 1 (quote-23-created).
[Worker-A1/sub-a] Processing quote 23 by Demo Author 1 (quote-23-created).
[Worker-A2/sub-a] Processing quote 24 by Demo Author 2 (quote-24-created).
[Worker-B1/sub-b] Processing quote 24 by Demo Author 2 (quote-24-created).
[Worker-A1/sub-a] Processing quote 25 by Demo Author 3 (quote-25-created).
[Worker-A2/sub-a] Processing quote 26 by Demo Author 4 (quote-26-created).
[Worker-B1/sub-b] Processing quote 25 by Demo Author 3 (quote-25-created).
[Worker-A1/sub-a] Failed to process quote-27-created (delivery attempt 1); abandoning for retry.
[Worker-B1/sub-b] Processing quote 26 by Demo Author 4 (quote-26-created).
[Worker-A2/sub-a] Failed to process quote-27-created (delivery attempt 2); abandoning for retry.
[Worker-A1/sub-a] Failed to process quote-27-created (delivery attempt 3); abandoning for retry.
[Worker-B1/sub-b] Failed to process quote-27-created (delivery attempt 1); abandoning for retry.
[Worker-B1/sub-b] Failed to process quote-27-created (delivery attempt 2); abandoning for retry.
[Worker-B1/sub-b] Failed to process quote-27-created (delivery attempt 3); abandoning for retry.
```

Two things worth pointing out in that log:

- **Competing consumers, proven, not just configured**: `quote-27-created`'s three delivery attempts on `sub-a` were handled by `Worker-A1`, then `Worker-A2`, then `Worker-A1` again — the same logical message bounced between the two competing workers across its abandon/redeliver cycle.
- **Idempotency, proven**: quote 2's second delivery on `sub-b` was recognized as already processed and skipped, without reprocessing.

**Proof the poison messages actually landed in the real Service Bus DLQ** (Azure CLI, not application state):

```text
$ az servicebus topic subscription show --namespace-name sb-day19-quotedemo \
    --resource-group thinkschool-rg --topic-name quote-events --name sub-a \
    --query "{name:name, maxDeliveryCount:maxDeliveryCount, deadLetter:countDetails.deadLetterMessageCount}"
{
  "deadLetter": 2,
  "maxDeliveryCount": 3,
  "name": "sub-a"
}
```

`sub-b` independently showed the identical result (`deadLetter: 2`) — each subscription dead-lettered its *own* copy of both poison messages, on its own delivery-count tracking, exactly as the two-independent-subscriptions model predicts.

## A Concrete Bug Caught During This Review

The first attempt registered the three subscription workers with `builder.Services.AddHostedService(sp => ...)`. Only `Worker-A1` ever started; `Worker-A2` and `Worker-B1` silently never ran. Diagnostic logging traced this to `AddHostedService`'s factory overload registering via `TryAddEnumerable`, keyed on the factory delegate's *return type* — since all three factories return `ServiceBusSubscriptionWorker`, only the first registration survived and the other two were silently dropped before ever reaching the DI container.

Fix: register these three with plain `builder.Services.AddSingleton<IHostedService>(factory)` instead, which has no such dedup and adds one independent entry per call. Verified afterward that all three workers start and that `sub-b`'s five backlogged messages (never drained while `Worker-B1` silently didn't exist) were processed once fixed.

## Tests and Build

```text
25/25 passed
```

New tests (`ProcessedMessageStoreTests.cs`) exercise the idempotency store against a real SQLite database — including the two-competing-consumers race — rather than a fake, since the correctness guarantee is the database's own unique constraint. All pre-existing Day 18 tests pass unchanged (one timing-sensitive shutdown test — `HostShutdown_DoesNotWaitOutInFlightSimulatedDelay`, already flagged in the Day 18 README as timing-fragile — flaked once under full-suite CPU contention and passed cleanly in isolation and on a repeat full run; unrelated to this change, since Service Bus registers zero extra hosted services in the `Testing` environment).

Backend build: **Passed**.

## What Would Break This

- Changing `QuoteCreatedEvent`'s shape without a compatible consumer deserialization path (no schema versioning is implemented here — this is a demo, not a contract-versioned event).
- Registering another hosted service via `AddHostedService(factory)` with the same return type as an existing one — see the dedup bug above; use `AddSingleton<IHostedService>(factory)` for multiple instances of one class.
- Deleting or lowering `MaxDeliveryCount` on `sub-a`/`sub-b` would change how many attempts a poison message survives before dead-lettering.
- Running without `az login` (or without the `Azure Service Bus Data Owner` role) locally, or without a managed identity/role assignment in Azure — publishing and consuming both fail authentication with no connection-string fallback.
- The `ProcessedMessages` table growing unbounded — this demo never prunes old idempotency records; production would want a retention/cleanup policy.

## Final Status

- [x] Topic (`quote-events`) and two independent subscriptions (`sub-a`, `sub-b`) — reused existing resources, none created or modified
- [x] Publisher: stable `MessageId`, async, cancellation-aware, disposes its sender via DI
- [x] Two subscriptions each independently receive every published message — proven live
- [x] Competing consumers within `sub-a` (`Worker-A1`/`Worker-A2`) — proven live, including a message bouncing between them across retries
- [x] Idempotent handling keyed on `MessageId`, scoped per subscription, DB-backed for correctness under competing consumers
- [x] Poison message retried 3 times then dead-lettered by the real Service Bus DLQ — verified via `az servicebus topic subscription show`
- [x] No secret, connection string, or key committed anywhere — identity-based auth only
- [x] Day 18 functionality and tests preserved unchanged
- [x] Backend tests: 25/25 passed
- [x] Build passed
