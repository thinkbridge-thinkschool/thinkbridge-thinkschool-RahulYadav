# Day 22 Piece 2 — Quote Collections as a Modular Monolith

## 1. Product Slice

**Quote Collections**: a user creates a named collection and adds/removes quotes to/from it (e.g. collection "Motivation" containing quotes 12, 27, 41). This slice was already emerging in the Day-22 Piece 1 codebase (a flat `Models/Collection.cs` + `Models/CollectionItem.cs` + `POST /api/collections` endpoint existed before this piece started), so it was used as-is rather than inventing a different capability — this piece restructures it into a proper modular monolith with a real aggregate instead of adding a new feature.

## 2. Architecture Decision

Single ASP.NET Core host, single deployable (`QuotesApi.csproj`), single SQLite database — organized as a **modular monolith**: business-capability modules under `QuotesAPI/Modules/*`, each with its own Domain/Application/Infrastructure/Contracts(/Presentation) layers, instead of one big set of `Controllers/`, `Services/`, `Repositories/` folders shared by everything. No microservices, no extra network hops, no per-module database. See `Day22_Piece2_Design.md` for the full one-page rationale (reproduced under §11 below).

## 3. Bounded Contexts

| Context | Responsibility | Owns |
|---|---|---|
| Quotes | Quote lifecycle/read model (pre-existing, Day 1–21) | `Quote`, exposed via `IQuoteCatalog` only |
| Collections | Create collections, manage quote membership | `Collection` aggregate, `QuoteMembership` |
| Notifications | React to Collections' integration events | `NotificationRecord` |

Quotes' underlying implementation (repository, EF entity, caching, resilience, outbox/Service-Bus publishing) predates this restructuring and is left exactly where it was (`QuotesAPI/Models`, `Repositories`, `Messaging`, `Caching`, `Resilience`) — it already has extensive test coverage from Days 1–22. `Modules/Quotes` adds only the thin `IQuoteCatalog` contract that turns it into a real module boundary other modules must go through.

## 4. Core Aggregate

**`Collection`** (`QuotesAPI/Modules/Collections/Domain/Aggregates/Collection.cs`):

```
Collection
 ├── Id
 ├── Name            (3–80 chars, non-empty)
 ├── OwnerId
 └── QuoteMemberships
       ├── QuoteId
       └── AddedAtUtc
```

Invariants enforced only inside the aggregate: name validity, no duplicate quote membership, max 50 memberships, remove requires an existing membership. Operations: `Create`, `Rename`, `AddQuote`, `RemoveQuote`. The Domain layer has no dependency on EF Core, ASP.NET Core, or any infrastructure package — verified by architecture tests, not just by convention.

## 5. Module Boundaries

- Presentation → Application → Domain; Infrastructure implements Application's ports.
- Collections depends on Quotes only through `Modules.Quotes.Contracts.IQuoteCatalog` — never `IQuoteRepository` or the `Quote` entity.
- Notifications depends on Collections only through `Modules.Collections.Contracts.Events` — never Collections' Domain, Application, or Infrastructure.
- No EF entity crosses a module boundary; only `Contracts/Dtos` and `Contracts/Events` records do.
- `QuotesDbContext` stays a single shared composition root (one database), but each module owns and applies its own `IEntityTypeConfiguration` (`CollectionEntityConfiguration`, `NotificationEntityConfiguration`) — "one database, many module-owned schemas," the modular-monolith analogue of a microservice's database-per-service.

## 6. Async Flows

```
FLOW 1 — CollectionCreated
HTTP POST /api/collections
  -> CreateCollectionCommandHandler -> Collection.Create -> EfCollectionRepository.AddAsync (persist)
  -> collection.MarkCreated + DequeueDomainEvents
  -> IIntegrationEventPublisher.PublishAsync(CollectionCreated)
  -> Notifications: CollectionCreatedNotificationHandler -> NotificationStore

FLOW 2 — QuoteAddedToCollection
HTTP POST /api/collections/{id}/items
  -> AddQuoteToCollectionCommandHandler -> IQuoteCatalog.FindAsync (sync cross-module call)
  -> Collection.AddQuote -> EfCollectionRepository.SaveAsync (persist)
  -> Publish QuoteAddedToCollection -> Notifications' handler -> NotificationStore

FLOW 3 — QuoteRemovedFromCollection
HTTP DELETE /api/collections/{id}/items/{quoteId}
  -> RemoveQuoteFromCollectionCommandHandler -> Collection.RemoveQuote -> EfCollectionRepository.SaveAsync (persist)
  -> Publish QuoteRemovedFromCollection -> Notifications' handler -> NotificationStore
```

Publishing goes through `Shared/Messaging/InProcessIntegrationEventPublisher` — an in-process, DI-resolved pub/sub keyed by event type, run synchronously in the same request. This keeps the demo deterministic and avoids inventing a second messaging stack: the existing Day 20 Transactional Outbox (`Messaging/OutboxRelayBackgroundService`) is what already gives this codebase durable cross-process delivery, and would be the natural swap-in behind `IIntegrationEventPublisher` if Notifications (or any module) were ever extracted into its own process — no Domain/Application code in the module would need to change.

`GET /api/notifications` (Notifications' own thin endpoint, not part of the suggested folder tree but added for observability) lets you see the recorded reactions after exercising the three flows above.

## 7. Scaffolded Solution Layout

```
Day-22/piece2/
├── QuotesAPI/
│   ├── Modules/
│   │   ├── Quotes/
│   │   │   ├── Application/QuoteCatalog.cs
│   │   │   ├── Contracts/IQuoteCatalog.cs, QuoteSummary.cs
│   │   │   └── QuotesModule.cs
│   │   │
│   │   ├── Collections/
│   │   │   ├── Domain/
│   │   │   │   ├── Aggregates/Collection.cs
│   │   │   │   ├── Entities/QuoteMembership.cs
│   │   │   │   └── Events/ (IDomainEvent, CollectionCreatedDomainEvent, QuoteAddedToCollectionDomainEvent, QuoteRemovedFromCollectionDomainEvent)
│   │   │   ├── Application/
│   │   │   │   ├── Commands/ (CreateCollectionCommand[Handler], AddQuoteToCollectionCommand[Handler], RemoveQuoteFromCollectionCommand[Handler])
│   │   │   │   ├── Queries/ (GetCollectionQuery[Handler])
│   │   │   │   ├── Ports/ICollectionRepository.cs
│   │   │   │   ├── Exceptions/ (CollectionNotFoundException, QuoteNotFoundException)
│   │   │   │   └── Mapping/CollectionMapper.cs
│   │   │   ├── Infrastructure/
│   │   │   │   ├── Persistence/CollectionEntityConfiguration.cs
│   │   │   │   └── Repositories/EfCollectionRepository.cs
│   │   │   ├── Contracts/
│   │   │   │   ├── Events/ (CollectionCreated, QuoteAddedToCollection, QuoteRemovedFromCollection)
│   │   │   │   └── Dtos/ (CollectionDto, QuoteMembershipDto)
│   │   │   ├── Presentation/Endpoints/CollectionEndpoints.cs
│   │   │   └── CollectionsModule.cs
│   │   │
│   │   └── Notifications/
│   │       ├── Application/
│   │       │   ├── EventHandlers/ (CollectionCreatedNotificationHandler, QuoteAddedToCollectionNotificationHandler, QuoteRemovedFromCollectionNotificationHandler)
│   │       │   └── Ports/INotificationStore.cs
│   │       ├── Contracts/NotificationDto.cs
│   │       ├── Infrastructure/ (NotificationStore.cs, Persistence/NotificationRecord.cs, Persistence/NotificationEntityConfiguration.cs)
│   │       ├── Presentation/Endpoints/NotificationEndpoints.cs
│   │       └── NotificationsModule.cs
│   │
│   ├── Shared/
│   │   └── Messaging/ (IIntegrationEvent, IIntegrationEventHandler, IIntegrationEventPublisher, InProcessIntegrationEventPublisher, SharedMessagingExtensions)
│   │
│   ├── Data/QuotesDbContext.cs        (unchanged location; applies each module's EF configuration)
│   ├── Repositories/, Messaging/, Caching/, Resilience/, BackgroundProcessing/, Authorization/  (pre-existing Quotes-era infrastructure, untouched)
│   └── Program.cs                     (composition root: AddSharedMessaging/AddQuotesModule/AddCollectionsModule/AddNotificationsModule)
│
└── QuotesAPI.Tests/
    ├── Architecture/ModuleBoundaryTests.cs
    ├── Collections/ (CollectionAggregateTests.cs, CollectionEndpointsTests.cs)
    ├── Notifications/NotificationFlowTests.cs
    └── (pre-existing Day 1–22 test files, unchanged)
```

## 8. Architecture Rules (enforced by tests)

- Domain does not depend on EF Core, ASP.NET Core/HTTP, Polly, StackExchange.Redis, or Azure.Messaging.ServiceBus.
- Domain does not depend on its own module's Application, Infrastructure, Presentation, or Contracts layers (one-way dependency).
- Collections does not depend on `QuotesApi.Repositories` or `QuotesApi.Models.Quote`/`QuoteCreationResult` directly.
- Notifications does not depend on Collections' Domain, Application, or Infrastructure.
- Notifications does depend on Collections' Contracts (positive control — proves the flow is actually wired, not accidentally disconnected).
- Collections' Contracts never reference Collections' Domain or Infrastructure.
- Collections' Presentation never references Collections' Domain or Infrastructure directly (only Application).

These are hand-rolled reflection checks (`QuotesAPI.Tests/Architecture/ModuleBoundaryTests.cs`) rather than a NetArchTest/ArchUnitNET dependency — the project didn't otherwise need one, and a single-assembly modular monolith needs *something* other than C# access modifiers to actually enforce these rules, since `internal` doesn't cross-module-isolate types compiled into one assembly.

## 9. Tests

- `Collections/CollectionAggregateTests.cs` — pure Domain unit tests (no DB, no HTTP): naming/ownership validation, duplicate/over-limit/missing-membership rules, domain event raising, `DequeueDomainEvents` semantics.
- `Collections/CollectionEndpointsTests.cs` — end-to-end HTTP tests through the real pipeline (`QuotesApiFactory`, real SQLite): create/get/add/remove happy paths and 400/404 error paths.
- `Notifications/NotificationFlowTests.cs` — end-to-end tests proving all three async flows: creating a collection, adding a quote, and removing a quote each produce a corresponding row visible at `GET /api/notifications`.
- `Architecture/ModuleBoundaryTests.cs` — the 7 rules listed in §8.
- All pre-existing Day 1–22 tests are unchanged and still pass against the refactored Collections module.

## 10. How to Run

```bash
cd Day-22/piece2/QuotesAPI
dotnet build

cd ../QuotesAPI.Tests
dotnet test                                              # full suite
dotnet test --filter "FullyQualifiedName~Architecture"   # architecture tests only
dotnet test --filter "FullyQualifiedName~Collections"    # Collections module tests only
dotnet test --filter "FullyQualifiedName~Notifications"  # Notifications flow tests only

cd ../QuotesAPI
dotnet run                                                # http://localhost:5xxx
# then, e.g.:
curl -X POST http://localhost:5xxx/api/collections -H "Content-Type: application/json" -d "{\"name\":\"Motivation\",\"ownerId\":1}"
curl http://localhost:5xxx/api/notifications
```

## 11. One-Page Design

See `Day22_Piece2_Design.md` in this directory for the full one-page design writeup (Product Slice / Architecture / Bounded Contexts / Core Aggregate / Async Flows / Module Rules / Why Modular Monolith).

## Repository

```
origin      https://github.com/rahulyadav753/thinkbridge-thinkschool.git
thinkbridge https://github.com/thinkbridge-thinkschool/thinkbridge-thinkschool-RahulYadav.git
thinkschool https://github.com/thinkbridge-thinkschool/thinkbridge-thinkschool-RahulYadav.git
```

(from `git remote -v` on this working copy; `origin` is the primary remote.)
