# Day 21 Piece 1 — HybridCache + Redis + Stampede Protection

## Objective

Front the hot read `GET /api/quotes/{id}` with Microsoft `HybridCache`
(L1 in-process memory + L2 Redis), prove stampede protection under
concurrency (N concurrent cold requests → one DB read, not N), and measure
the real before/after DB load and latency.

## Architecture

```
Request
  ↓
QuoteCacheReader.GetByIdAsync
  ↓
HybridCache.GetOrCreateAsync("quote:{id}")
  ├── L1 in-memory cache (per instance)
  └── L2 Redis (when ConnectionStrings:Redis is configured)
        ↓ cache miss
     factory runs → IQuoteRepository.GetByIdAsync → EF Core → SQLite
        ↓
     result populates L1 (+ L2 if configured)
        ↓
     CachedQuote returned to every coalesced caller
```

Concurrent requests for the same cold key:

```
50 concurrent GET /api/quotes/{id}, same id, cold key
        ↓
HybridCache.GetOrCreateAsync single-flight
        ↓
ONE factory execution → ONE DB read
        ↓
50 successful responses, all with the same quote
```

## Cache Wiring

`Extensions/CachingExtensions.cs` (called from `Program.cs` as
`builder.Services.AddQuoteCaching(builder.Configuration)`):

```csharp
public static IServiceCollection AddQuoteCaching(
    this IServiceCollection services,
    IConfiguration configuration)
{
    var redisConnectionString = configuration.GetConnectionString("Redis");

    if (!string.IsNullOrWhiteSpace(redisConnectionString))
    {
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = "QuotesApi:";
        });
    }

    var cacheOptions =
        configuration.GetSection("QuoteCache").Get<QuoteCacheOptions>()
        ?? new QuoteCacheOptions();

    services.AddHybridCache(options =>
    {
        options.DefaultEntryOptions = new HybridCacheEntryOptions
        {
            Expiration = cacheOptions.Expiration,
            LocalCacheExpiration = cacheOptions.LocalCacheExpiration
        };

        options.MaximumPayloadBytes = 64 * 1024;
    });

    services.AddSingleton<QuoteCacheMetrics>();
    services.AddScoped<QuoteCacheReader>();

    return services;
}
```

`Program.cs` calls this alongside the existing `AddInfrastructure(...)` call,
with a comment block explaining the same conditional-Redis pattern already
used for Service Bus in this project.

`ConnectionStrings:Redis` is read from configuration only. It is `""` in
`appsettings.json` (and unset in `appsettings.Testing.json`/
`appsettings.Development.json`) — never a hardcoded host, password, or
connection string. Production supplies it via environment variable, Azure
Key Vault, or App Settings, the same way `Jwt:Key` and
`ApplicationInsightsConnectionString` already are in this project.

`QuoteCacheOptions` (`Options/QuoteCacheOptions.cs`) binds the
`"QuoteCache"` section (`Expiration`, `LocalCacheExpiration`), defaulting to
30 seconds. `appsettings.Testing.json` overrides both to 1 second so the
expiration test doesn't need a 30-second sleep.

## Cached Endpoint

`Extensions/QuoteEndpointExtensions.cs`, `GET /api/quotes/{id}`:

```csharp
group.MapGet("/{id:int}", async (
    int id,
    QuoteCacheReader cacheReader,
    CancellationToken cancellationToken) =>
{
    var quote = await cacheReader.GetByIdAsync(id, cancellationToken);

    return quote is null
        ? Results.NotFound()
        : Results.Ok(quote);
});
```

`Caching/QuoteCacheReader.cs` is where the cache/DB boundary actually lives:

```csharp
public async Task<CachedQuote?> GetByIdAsync(int id, CancellationToken cancellationToken)
{
    var state = new FactoryState(id, _repository, _metrics);

    var cached = await _cache.GetOrCreateAsync(
        CacheKeys.Quote(id),
        state,
        static async (s, ct) =>
        {
            // Reached only by the single request HybridCache elects to
            // actually populate this key.
            s.FactoryRan = true;
            s.Metrics.RecordFactoryExecution();

            var quote = await s.Repository.GetByIdAsync(s.Id, ct);

            return quote is null ? null : CachedQuote.FromQuote(quote);
        },
        cancellationToken: cancellationToken);

    if (state.FactoryRan) _metrics.RecordMiss(); else _metrics.RecordHit();

    return cached;
}
```

The response DTO is `CachedQuote` (`Models/CachedQuote.cs`) — `Id`, `Author`,
`Text`, `IsDeleted` — a plain record, not the EF-tracked `Quote` entity
(`Quote`'s only constructor is private, so it can't round-trip through
`System.Text.Json`/Redis anyway). The JSON shape returned to clients is
unchanged from Day 20.

`DELETE /api/quotes/{id}` now also calls `cacheReader.EvictAsync(id, ...)`
on a successful delete, so a soft-deleted quote doesn't keep being served
from a stale cache entry until the TTL expires.

## Cache Key

`Caching/CacheKeys.cs`: `quote:{id}` — deterministic and stable across
instances that share the same Redis L2.

## Stampede Protection

`HybridCache.GetOrCreateAsync` coalesces concurrent calls for the same key:
when a key is cold and N callers ask for it at once, only one of them
actually runs the factory delegate (the DB read); every other caller
awaits that same in-flight operation and receives its result. This is
HybridCache's built-in single-flight behavior — no `ConcurrentDictionary`,
`SemaphoreSlim`, or other homemade locking was added.

**Scope of the guarantee**: this is a single-process (single
`HybridCache`/L1) guarantee. It coalesces every concurrent request landing
on the same running instance. It is **not** a distributed exclusive lock —
if two separate application instances both see a cold key at the same
moment, each independently runs its own single factory execution, unless a
shared Redis L2 already has a fresh value for one of them to hit. This
exercise proves the single-instance guarantee (see below); it does not
claim cross-instance exactly-once DB access.

### Proof: `HybridCacheStampedeTests.HybridCache_ConcurrentColdRequests_CoalesceDatabaseLoad`

The test fires 50 concurrent `GET /api/quotes/{id}` requests for the same
cold id against the real ASP.NET Core pipeline (`QuotesApiFactory`), with
the real `QuoteRepository`/SQLite read wrapped in a `TaskCompletionSource`
gate (`GatedQuoteRepository`) that only the actual DB read is held behind
— HybridCache's own coalescing logic runs unmodified. The gate guarantees
every one of the 50 requests has reached `GetOrCreateAsync` before the
single leader's read is allowed to finish, so the result isn't a lucky
timing race.

Measured output (`dotnet test --filter FullyQualifiedName~HybridCacheStampedeTests --logger "console;verbosity=detailed"`):

```
=== HybridCache stampede protection evidence ===
Concurrent requests: 50
Cache key: quote:1
Repository entries (should be 1): 1
Factory executions: 1
DB quote queries: 1
Successful responses: 50
```

Re-run 5 times consecutively with no failures (see Verification below).

## Database Query Instrumentation

`Data/DbQueryCounterInterceptor.cs` is a real EF Core `DbCommandInterceptor`,
registered on `QuotesDbContext` in `Extensions/InfrastructureExtensions.cs`:

```csharp
services.AddSingleton<DbQueryCounter>();

services.AddDbContext<QuotesDbContext>((serviceProvider, options) =>
{
    options.UseSqlite(configuration.GetConnectionString("DefaultConnection"));

    options.AddInterceptors(
        new DbQueryCounterInterceptor(
            serviceProvider.GetRequiredService<DbQueryCounter>()));
});
```

`DbQueryCounter` (`Data/DbQueryCounter.cs`) counts every `DbDataReader`
command SQLite actually executes (`ReaderExecuted`/`ReaderExecutedAsync`),
classifying a command as a "quote read" only if its SQL text is a `SELECT`
against `"Quotes"`. This counts real database commands, not
repository/application method calls — a cache hit never reaches EF Core or
SQLite at all, so it can never be miscounted as a DB query.

## Cache Metrics

`Caching/QuoteCacheMetrics.cs` is a singleton the `QuoteCacheReader` updates
per request:

- **Miss**: this request's own call to `GetOrCreateAsync` is the one that
  ran the factory (it caused a DB read).
- **Hit**: this request's call did *not* run the factory — either the value
  was already cached, or the request was coalesced behind another
  in-flight factory execution by stampede protection.

This framing is deliberate: "hit" directly means "this request did not
cause database load," which is the thing this exercise is measuring.
`FactoryExecutions` is the count of actual factory runs (correlates almost
1:1 with `DatabaseQueries`).

`GET /api/diagnostics/cache` (`Extensions/DiagnosticsEndpointExtensions.cs`)
exposes:

```json
{
  "hits": 199,
  "misses": 1,
  "totalRequests": 200,
  "hitRate": 0.995,
  "factoryExecutions": 1,
  "databaseQueries": 1,
  "totalDatabaseCommands": 3,
  "totalDatabaseElapsedMs": 0.4
}
```

No secrets or Redis connection details are exposed — only counters.

Two supporting endpoints exist purely for benchmark/test reproducibility,
not as public API surface:
- `POST /api/diagnostics/cache/reset` — resets the counters (not the cache).
- `POST /api/diagnostics/cache/evict/{id}` — evicts one quote's cache entry
  on demand, so a "cold cache" run doesn't have to wait out the TTL.
- `GET /api/diagnostics/quotes/{id}/uncached` — reads through the identical
  repository/DbContext path but always bypasses HybridCache. This is what
  the "before" load-test measurements hit; the real public
  `GET /api/quotes/{id}` always goes through the cache in every
  environment, so the comparison's only variable is caching.

## Load Test

`QuotesAPI.Tests/HybridCacheLoadTests.cs`,
`LoadTest_BeforeVsAfter_MeasuresDbLoadAndLatency`:

- Endpoint: `GET /api/quotes/{id}` (after) vs
  `GET /api/diagnostics/quotes/{id}/uncached` (before) — same id, same
  repository/DbContext code path.
- Requests: 200, Concurrency: 50 (bounded via `SemaphoreSlim`).
- Runs against the real ASP.NET Core pipeline in-process
  (`WebApplicationFactory`/`TestServer`) — no real network hop, so absolute
  latencies are lower than a deployed service, but both runs share the same
  process/DB, so the comparison isolates the caching effect.
- "After" run evicts the key first (`POST /api/diagnostics/cache/evict/{id}`)
  so it includes one genuine cold miss, not a pre-warmed cache.
- p99 is computed from the actual sorted array of 200 per-request
  latencies (`Array.Sort` + 99th-percentile index), not estimated from an
  average.
- Counters are reset (`POST /api/diagnostics/cache/reset`) between the
  before and after runs so each measurement window is clean.

## Before vs After

Measured by running
`dotnet test --filter FullyQualifiedName~HybridCacheLoadTests --logger "console;verbosity=detailed"`
twice in a row (both runs shown to demonstrate the numbers aren't cherry-picked):

**Run 1**

| Metric | Before (cache bypassed) | After (HybridCache) |
|---|---:|---:|
| Requests | 200 | 200 |
| Concurrency | 50 | 50 |
| Successful requests | 200 | 200 |
| DB queries | 200 | 1 |
| DB queries/sec | 542.0 | 10.7 |
| p99 latency | 207.18 ms | 76.35 ms |
| Cache hit rate | N/A | 99.50% |
| Total duration | 369.0 ms | 93.3 ms |

**Run 2**

| Metric | Before (cache bypassed) | After (HybridCache) |
|---|---:|---:|
| Requests | 200 | 200 |
| Concurrency | 50 | 50 |
| Successful requests | 200 | 200 |
| DB queries | 200 | 1 |
| DB queries/sec | 708.5 | 7.2 |
| p99 latency | 182.75 ms | 126.85 ms |
| Cache hit rate | N/A | 99.50% |
| Total duration | 282.3 ms | 139.7 ms |

Note on "DB queries/sec": this is `DB queries ÷ total run duration`. In the
"after" case the single DB query happens almost immediately and the
remaining 199 requests are pure in-memory cache hits, so this number is not
a meaningful throughput ceiling for HybridCache — the load-bearing number
is **DB queries** (200 → 1) and **DB load reduction**, not queries/sec, which
naturally look noisy over a ~100-140ms window with only 1 query in it.

p99 latency also drops (run 1: 207ms → 76ms) because 199 of the 200 "after"
requests never touch SQLite at all — they're served from the L1 in-memory
cache.

## DB Load Reduction

```
DB Load Reduction % = ((Before DB queries - After DB queries) / Before DB queries) * 100
Run 1: ((200 - 1) / 200) * 100 = 99.50%
Run 2: ((200 - 1) / 200) * 100 = 99.50%
```

## Stampede Proof

Reproduced from `HybridCacheStampedeTests` (also see Stampede Protection
above):

```
Concurrent requests: 50
Cache key: quote:1
Factory executions: 1
DB quote queries: 1
Successful responses: 50
```

Run 5 times consecutively (`dotnet test --filter FullyQualifiedName~HybridCacheStampedeTests --no-build`,
repeated): 5/5 passed, ~11s each, no flakiness observed.

## Why It Works

- **L1 (in-process memory)**: fastest path, per-instance, configured via
  `HybridCacheOptions.DefaultEntryOptions.LocalCacheExpiration`.
- **L2 (Redis, when `ConnectionStrings:Redis` is set)**: shared across
  instances, lets a cache warmed by one instance serve another instance's
  request without hitting the database — `HybridCache` uses whatever
  `IDistributedCache` is registered as L2 automatically; no code at the
  call site changes when Redis is added or removed.
- **Stampede protection**: `HybridCache.GetOrCreateAsync` keeps an internal
  table of in-flight factory executions keyed by cache key. A caller that
  finds an existing in-flight entry for its key awaits that same `Task`
  instead of starting a new one. This is deterministic (not
  best-effort/racy) — it is exactly what makes the 50-concurrent-request
  test above reproducible at 1 DB query every run, not "usually low."

## Failure Considerations

- **Redis unavailable**: `AddStackExchangeRedisCache` is only registered
  when `ConnectionStrings:Redis` is non-empty. When it's empty (as in this
  repository's Testing/Development configuration, and as verified by every
  test in this piece), `HybridCache` still works correctly — it simply has
  no L2 to fall back to, so every process keeps its own L1 only. **This
  environment has no Redis server available** (no `docker`, no local Redis
  binary), so the Redis/L2 code path was verified by inspection and
  successful compilation/startup with an empty connection string, but the
  actual Redis round-trip (a live L2 hit/miss, or Redis becoming
  unavailable mid-request) was **not** exercised here. That is an honest
  gap in this environment, not a claim about untested code behaving a
  particular way.
- **DB unavailable**: the factory delegate simply throws (EF Core/SQLite
  exception propagates through `GetOrCreateAsync`), and `HybridCache` does
  not cache a failed factory execution — the next request retries the
  factory rather than being stuck with a poisoned cache entry. No infinite
  retry loop was added.
- **Cache factory throws**: same as above — `GetOrCreateAsync` propagates
  the exception to the caller(s) awaiting that in-flight operation; nothing
  is cached.
- **Cancellation**: the request's `CancellationToken` is threaded through
  to `GetOrCreateAsync` and into the factory/repository call, same as every
  other endpoint in this project.
- **Not-found caching**: a miss where the repository returns `null` (no
  such quote) is cached as `null` for the same TTL as a found quote — a
  quote created with an id a client had *just* queried (and gotten a 404
  for) would keep 404ing until the entry expires. Given ids are DB
  auto-increment and never reused, this is not a realistic scenario in
  practice, but it's a real, documented tradeoff, not swept under the rug.
- **Delete correctness**: `DELETE /api/quotes/{id}` evicts the cache entry
  on success (see Cached Endpoint above) specifically so a delete is never
  masked by a stale cached read.
- No distributed lock is claimed anywhere in this piece — see Stampede
  Protection's "scope of the guarantee" note.

## Verification

Exact commands run, from `Day-21/piece1/`:

```
dotnet build QuotesAPI/QuotesApi.csproj
  → Build succeeded. 0 Errors, 2 pre-existing NU1903 warnings
    (SQLitePCLRaw.lib.e_sqlite3 advisory — present before this change too,
    unrelated to Day 21).

dotnet test QuotesAPI.Tests/QuotesApi.Tests.csproj
  → Passed! 41/41, Duration ~2m 57s (assembly parallelization disabled —
    see "Real bug found and fixed" below).

dotnet test QuotesAPI.Tests/QuotesApi.Tests.csproj --filter "FullyQualifiedName~HybridCacheStampedeTests" --no-build
  → run 5 times consecutively: 5/5 passed, ~11s each.

dotnet test QuotesAPI.Tests/QuotesApi.Tests.csproj --filter "FullyQualifiedName~HybridCacheLoadTests" --logger "console;verbosity=detailed"
  → run twice: both passed, numbers captured verbatim above.
```

### Real bug found and fixed

Adding the concurrency-heavy stampede and load tests exposed a pre-existing
flaky test: `QuoteProcessingHttpTests.HostShutdown_DoesNotWaitOutInFlightSimulatedDelay`
(a Day 18 test, unmodified by this piece) asserts host-shutdown overhead is
under a 6-second tolerance. It passes reliably alone, but running the full
suite under xUnit's default test-class parallelism let the new
concurrency-heavy tests starve it of CPU, pushing shutdown overhead to
~6.6s and failing it — reproduced twice. This is a side effect of the load
this piece intentionally adds, not a functional regression in Day 18/19/20
behavior (no Day 18 code or test logic was changed). Fixed by adding
`QuotesAPI.Tests/AssemblyInfo.cs` with
`[assembly: CollectionBehavior(DisableTestParallelization = true)]`, which
serializes the test assembly — a standard, safe practice for
`WebApplicationFactory` integration-test suites — and does not change any
test's assertions or thresholds. Re-verified: 41/41 passing after the fix.

### Tests added (Phase 11 coverage)

| # | Requirement | Test |
|---|---|---|
| 1-3 | Cache miss reads DB; cache hit does not | `HybridCacheTests.HybridCache_ColdRequest_ReadsDatabaseExactlyOnce`, `HybridCache_WarmRequest_ServedFromCacheWithoutAdditionalDatabaseQuery` |
| 4 | Expiration causes refresh | `HybridCacheTests.HybridCache_EntryExpires_CausesRefreshOnNextRequest` |
| 5-6 | Concurrent cold requests coalesced; same key → same quote | `HybridCacheStampedeTests.HybridCache_ConcurrentColdRequests_CoalesceDatabaseLoad` |
| 7 | Metrics/hit rate correct | `HybridCacheTests.CacheDiagnostics_ReportsAccurateHitMissAndHitRate` |
| 8 | Day 19 Service Bus tests still pass | Verified as part of the 41/41 full-suite run (unmodified) |
| 9 | Day 20 Outbox tests still pass | Verified as part of the 41/41 full-suite run (unmodified) |
| bonus | Delete evicts cache | `HybridCacheTests.DeleteQuote_EvictsCacheEntry_SubsequentGetIsNotFound` |
| bonus | Before/after benchmark | `HybridCacheLoadTests.LoadTest_BeforeVsAfter_MeasuresDbLoadAndLatency` |

No test sleeps to assert an outcome by chance: the stampede test uses a
`TaskCompletionSource` gate (`GatedQuoteRepository`) so coalescing is
deterministic regardless of machine speed; the only real-time waits are the
expiration test's bounded 1.5s delay against a 1s configured TTL (inherent
to testing a TTL at all) and the stampede test's 500ms dispatch window
(bounded by the gate never completing early, not by luck).
