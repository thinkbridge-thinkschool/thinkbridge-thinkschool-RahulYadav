# Day 22 Piece 2 — Modular Monolith Design

## Product Slice
Quote Collections — a user creates a named collection of quotes and adds/removes quotes from it. The existing Day-22 Piece 1 codebase already had a flat `Collection`/`CollectionItem` model and a `POST /api/collections` endpoint, confirming this is the codebase's own natural next capability rather than an invented one.

## Architecture
This is a **modular monolith**, not microservices: one ASP.NET Core host (`QuotesApi.csproj`), one process, one deployment, one SQLite database — but the code is organized around business capabilities (bounded contexts) instead of technical layers. Each module under `QuotesAPI/Modules/*` owns its own domain model, application/use-case layer, persistence mapping, and public contracts; nothing outside a module reaches into its internals. Modules talk to each other only through explicit contracts (a public interface for synchronous calls, integration events for asynchronous reactions) — never through each other's EF entities or repositories. Because everything still runs in one process, there is no network hop, no distributed transaction, and no separate deployment pipeline per module — the boundaries are architectural discipline (enforced by automated tests, see below), not physical isolation.

## Bounded Contexts

| Context | Responsibility | Owns |
|---|---|---|
| Quotes | Quote lifecycle/read model (pre-existing, Day 1–21) | `Quote`, exposed to other modules only via `IQuoteCatalog` |
| Collections | Create collections and manage quote membership | `Collection` aggregate, `QuoteMembership` |
| Notifications | React to Collections' integration events | `NotificationRecord` |

## Core Aggregate

**`Collection`** (`Modules/Collections/Domain/Aggregates/Collection.cs`) is the aggregate root and consistency boundary for a collection's membership.

- **Invariants**: name is 3–80 chars and non-empty; a quote can belong at most once (`AddQuote` rejects duplicates); a collection cannot exceed `MaxQuoteMemberships` (50); `RemoveQuote` rejects a quote that isn't a member.
- **Consistency boundary**: `QuoteMemberships` is a private list exposed read-only; the only ways to mutate it are `AddQuote`/`RemoveQuote`, so "no duplicate membership" is a guarantee, not a convention.
- **Operations**: `Create`, `Rename`, `AddQuote`, `RemoveQuote` — plus `MarkCreated`/`DequeueDomainEvents` for raising the domain events (`CollectionCreatedDomainEvent`, `QuoteAddedToCollectionDomainEvent`, `QuoteRemovedFromCollectionDomainEvent`) the Application layer translates into public integration events. The Domain layer has zero dependency on EF Core, ASP.NET Core, or any infrastructure package.

## Async Flows

```
FLOW 1 — CollectionCreated
HTTP POST /api/collections
  -> CreateCollectionCommandHandler
  -> Collection.Create (Domain)
  -> EfCollectionRepository.AddAsync (persist; DB assigns Id)
  -> collection.MarkCreated + DequeueDomainEvents
  -> IIntegrationEventPublisher.PublishAsync(CollectionCreated)
  -> Notifications' CollectionCreatedNotificationHandler -> NotificationStore

FLOW 2 — QuoteAddedToCollection
HTTP POST /api/collections/{id}/items
  -> AddQuoteToCollectionCommandHandler
  -> IQuoteCatalog.FindAsync (sync cross-module call into Quotes)
  -> Collection.AddQuote (Domain)
  -> EfCollectionRepository.SaveAsync (persist)
  -> Publish QuoteAddedToCollection
  -> Notifications' handler -> NotificationStore

FLOW 3 — QuoteRemovedFromCollection
HTTP DELETE /api/collections/{id}/items/{quoteId}
  -> RemoveQuoteFromCollectionCommandHandler
  -> Collection.RemoveQuote (Domain)
  -> EfCollectionRepository.SaveAsync (persist)
  -> Publish QuoteRemovedFromCollection
  -> Notifications' handler -> NotificationStore
```

All three publish through `Shared/Messaging/InProcessIntegrationEventPublisher` — an in-process, DI-resolved pub/sub (handlers looked up by event type) that keeps Collections and Notifications decoupled without a message broker. It runs synchronously within the same request (deterministic for tests, no polling), which is a deliberate scope choice for this scaffold; the Day 20 Transactional Outbox already in this codebase (`Messaging/OutboxRelayBackgroundService`) remains the durability mechanism if any of these events ever need to cross a process boundary — nothing in a module's own code would change, since it only depends on `IIntegrationEventPublisher`.

## Module Rules

- Domain depends on nothing outside the .NET BCL — no EF Core, ASP.NET Core, HttpClient, Redis, Service Bus, or Polly.
- Dependency direction inside a module: Presentation → Application → Domain; Infrastructure implements Application's ports.
- Collections never references `QuotesApi.Repositories.IQuoteRepository` or `QuotesApi.Models.Quote` — only `Modules.Quotes.Contracts.IQuoteCatalog`.
- Notifications never references Collections' Domain, Application, or Infrastructure — only `Modules.Collections.Contracts.Events`.
- Contracts (DTOs/events) never expose a Domain aggregate or EF entity.
- No module shares a repository or EF entity with another module; `QuotesDbContext` stays a single composition root, but each module owns and applies its own `IEntityTypeConfiguration`.

These rules are enforced by reflection-based architecture tests (`QuotesAPI.Tests/Architecture/ModuleBoundaryTests.cs`), since a single-assembly modular monolith cannot rely on C# `internal` alone to keep modules apart.

## Why Modular Monolith

One deployable unit means one CI/CD pipeline, one set of health checks, and no distributed-transaction problem when a request touches two modules (Collections' write and Notifications' write share the same DbContext/process). Cross-module calls are in-process method calls, not network round-trips, so there's no added latency or partial-failure mode to design around yet. The boundaries are still real — enforced by tests, not just convention — so if a module (e.g. Notifications) ever needs independent scaling or deployment, extracting it means swapping `InProcessIntegrationEventPublisher` for the existing outbox/Service Bus publisher; the module's own Domain/Application code does not change. This is not a claim that the modules already are microservices — there is no network boundary, no independent data store, and no independent deployment today.
