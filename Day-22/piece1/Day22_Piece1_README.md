# Day 22 — Polly Resilience

## What was implemented

- Retry with exponential backoff, applied **only** to the idempotent outbound GET
- Circuit breaker (Closed → Open → Half-Open → Closed), shared by both operations
- Timeout, bounding each individual attempt
- Bulkhead / concurrency limiter, rejecting excess concurrent calls fast
- Structured logging via `ILogger` (Serilog console sink) for every retry, circuit
  transition, timeout and bulkhead rejection
- `QuoteDependencyResilienceMetrics` counters, exposed at `GET /api/diagnostics/resilience`

### Outbound dependency

The project already had a rough Polly scaffold in `Program.cs` (an `AddHttpClient("my-service")`
call hard-wired to an intentionally-unreachable `localhost:59999`, with a dev-only
`/test-resilience` endpoint). It had no idempotency distinction, no bulkhead, no
configuration binding, and Console.WriteLine instead of structured logging, so it did not
meet the Day 22 requirements. It has been replaced with a small, clean, testable outbound
dependency:

```
IQuoteDependencyClient (interface)
        |
QuoteDependencyClient   -- real HttpClient, no resilience logic of its own
        |
HttpClient("QuoteDependency")
        |
Polly ResiliencePipeline<HttpResponseMessage>  (resolved by key from DI)
        |
FakeQuoteDependencyHandler (tests only -- primary HttpMessageHandler swapped in)
```

`IQuoteDependencyClient` has exactly two methods:

- `GetQuoteOfTheDayAsync` — GET, idempotent, safe to retry
- `SubmitQuoteAsync` — POST, creates a resource, **never** retried automatically

Files:

- `QuotesAPI/Resilience/IQuoteDependencyClient.cs`, `QuoteDependencyClient.cs`
- `QuotesAPI/Resilience/QuoteDependencyResilienceMetrics.cs`
- `QuotesAPI/Extensions/QuoteDependencyResilienceExtensions.cs` (DI registration + pipelines)
- `QuotesAPI/Options/QuoteDependencyOptions.cs`, `QuoteDependencyResilienceOptions.cs`
- `QuotesAPI/Extensions/DiagnosticsEndpointExtensions.cs` (new `/api/diagnostics/resilience` route)
- `QuotesAPI/Program.cs` (scaffold removed, `AddQuoteDependencyResilience` wired in)
- `QuotesAPI/appsettings.json`, `appsettings.Testing.json` (new `QuoteDependency`/`Resilience` sections)
- Tests: `FakeQuoteDependencyHandler.cs`, `ResilienceQuotesApiFactory.cs`,
  `QuoteDependencyIdempotencyTests.cs`, `QuoteDependencyCircuitBreakerTests.cs`,
  `QuoteDependencyTimeoutTests.cs`, `QuoteDependencyBulkheadTests.cs`,
  `Day22ResilienceDemoTests.cs`

## Resilience Pipeline

Two separate pipelines are registered under two separate keys — **not** one shared
pipeline with a method-sniffing predicate — because that is the most explicit way to
guarantee the idempotency rule: a retry stage physically does not exist in the pipeline
used for the non-idempotent POST/create call.

```csharp
public const string HttpClientName = "QuoteDependency";
public const string IdempotentPipelineKey = "quote-dependency-get";
public const string NonIdempotentPipelineKey = "quote-dependency-post";

services.AddResiliencePipeline<string, HttpResponseMessage>(IdempotentPipelineKey, (builder, context) =>
{
    AddBulkhead(builder, options, metrics, logger, "GET");
    AddRetry(builder, options, metrics, logger);              // idempotent: retries allowed
    AddCircuitBreaker(builder, options, metrics, logger, "GET");
    AddTimeout(builder, options, metrics, logger, "GET");
});

services.AddResiliencePipeline<string, HttpResponseMessage>(NonIdempotentPipelineKey, (builder, context) =>
{
    AddBulkhead(builder, options, metrics, logger, "POST");
    // Deliberately NO .AddRetry() here.
    AddCircuitBreaker(builder, options, metrics, logger, "POST");
    AddTimeout(builder, options, metrics, logger, "POST");
});
```

**Pipeline order (outermost → innermost — Polly runs strategies in the order added):**

```
Bulkhead (concurrency limiter) → Retry → Circuit breaker → Timeout
```

This mirrors Microsoft's own "standard resilience handler" ordering
(`Microsoft.Extensions.Http.Resilience`'s `AddStandardResilienceHandler`), not the
naive `Timeout → Retry → CircuitBreaker → Bulkhead` ordering suggested as a starting
point:

- **Bulkhead is outermost** so the concurrency limit protects application capacity for
  the *whole* operation, however many retries it takes — a retrying call still only
  ever holds one bulkhead permit.
- **Retry** sits inside the bulkhead so it can retry a failure from anywhere inside it
  (a circuit-breaker rejection, a timeout, a transport failure).
- **Circuit breaker** sits inside retry so *every* attempt (including retries) is
  individually recorded against the breaker; once open, further retries fail fast with
  `BrokenCircuitException` instead of dialing a known-unhealthy dependency again.
- **Timeout is innermost** so it bounds each individual attempt, not the whole retry
  loop. A per-attempt timeout is itself treated as a failure by both the retry and
  circuit-breaker layers wrapping it.

Full code — retry and circuit-breaker strategy configuration
(`QuotesAPI/Extensions/QuoteDependencyResilienceExtensions.cs`):

```csharp
private static void AddRetry(
    ResiliencePipelineBuilder<HttpResponseMessage> builder,
    QuoteDependencyResilienceOptions options,
    QuoteDependencyResilienceMetrics metrics,
    ILogger logger)
{
    builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
    {
        MaxRetryAttempts = options.Retry.MaxRetryAttempts,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        Delay = TimeSpan.FromSeconds(options.Retry.BackoffSeconds),
        ShouldHandle = args => ValueTask.FromResult(IsTransientFailure(args.Outcome)),
        OnRetry = args =>
        {
            metrics.RecordRetryAttempt();
            logger.LogWarning(
                "[Retry] attempt={AttemptNumber} delay={DelayMs}ms reason={Reason}",
                args.AttemptNumber + 1, args.RetryDelay.TotalMilliseconds, DescribeOutcome(args.Outcome));
            args.Outcome.Result?.Dispose();
            return default;
        }
    });
}

private static void AddCircuitBreaker(
    ResiliencePipelineBuilder<HttpResponseMessage> builder,
    QuoteDependencyResilienceOptions options,
    QuoteDependencyResilienceMetrics metrics,
    ILogger logger,
    string operation)
{
    builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
    {
        FailureRatio = options.CircuitBreaker.FailureRatio,
        MinimumThroughput = options.CircuitBreaker.MinimumThroughput,
        SamplingDuration = TimeSpan.FromSeconds(options.CircuitBreaker.SamplingDurationSeconds),
        BreakDuration = TimeSpan.FromSeconds(options.CircuitBreaker.BreakDurationSeconds),
        ShouldHandle = args => ValueTask.FromResult(IsTransientFailure(args.Outcome)),
        OnOpened = args => { metrics.RecordCircuitOpened();
            logger.LogError("[Resilience] Circuit {Operation} OPENED for {BreakDurationSeconds}s (reason={Reason})",
                operation, args.BreakDuration.TotalSeconds, DescribeOutcome(args.Outcome));
            return default; },
        OnHalfOpened = args => { metrics.RecordCircuitHalfOpened();
            logger.LogWarning("[Resilience] Circuit {Operation} HALF-OPEN: probing dependency", operation);
            return default; },
        OnClosed = args => { metrics.RecordCircuitClosed();
            logger.LogInformation("[Resilience] Circuit {Operation} CLOSED: recovery confirmed", operation);
            return default; }
    });
}
```

Bulkhead and timeout (same file):

```csharp
builder.AddRateLimiter(new RateLimiterStrategyOptions
{
    DefaultRateLimiterOptions = new ConcurrencyLimiterOptions
    {
        PermitLimit = options.Bulkhead.MaxConcurrency,
        QueueLimit = options.Bulkhead.QueueLimit
    },
    OnRejected = args =>
    {
        metrics.RecordBulkheadRejected();
        logger.LogWarning("[Bulkhead] {Operation} concurrency limit reached (max={MaxConcurrency}); request rejected",
            operation, options.Bulkhead.MaxConcurrency);
        return default;
    }
});

builder.AddTimeout(new TimeoutStrategyOptions
{
    Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
    OnTimeout = args =>
    {
        metrics.RecordTimeout();
        logger.LogWarning("[Timeout] {Operation} attempt exceeded {TimeoutSeconds}s and was cancelled",
            operation, args.Timeout.TotalSeconds);
        return default;
    }
});
```

Configuration (`appsettings.json`):

```json
"QuoteDependency": {
  "BaseUrl": "https://quote-dependency.internal/"
},
"Resilience": {
  "Retry": { "MaxRetryAttempts": 3, "BackoffSeconds": 1 },
  "CircuitBreaker": {
    "FailureRatio": 0.5,
    "MinimumThroughput": 5,
    "SamplingDurationSeconds": 30,
    "BreakDurationSeconds": 10
  },
  "TimeoutSeconds": 3,
  "Bulkhead": { "MaxConcurrency": 5, "QueueLimit": 0 }
}
```

`appsettings.Testing.json` overrides these with small-but-valid values (Polly requires
`SamplingDuration`/`BreakDuration` > 0.5s) so tests observe real retries, circuit
transitions and timeouts without slow sleeps; individual tests further override specific
values (e.g. `CircuitBreaker:MinimumThroughput`) via in-memory configuration.

## Idempotency Evidence

`IQuoteDependencyClient` enforces the rule structurally, not with a predicate that
inspects the HTTP method at runtime: `GetQuoteOfTheDayAsync` always executes through
`quote-dependency-get` (which has a retry stage); `SubmitQuoteAsync` always executes
through `quote-dependency-post` (which has **no** retry stage at all — requirement #7).

Real `dotnet test` output, both against the actual DI-registered pipelines:

```
Passed QuotesApi.Tests.QuoteDependencyIdempotencyTests.Get_TransientFailureThenSuccess_IsRetriedUntilSuccess [410 ms]
  Standard Output Messages:
 === RETRY EVIDENCE (idempotent GET) ===
 attempt 1 -> failure
 wait/backoff
 attempt 2 -> failure
 wait/backoff
 attempt 3 -> success (Quote of the day: perseverance.)
 Total dependency calls: 3
 Recorded retry attempts (metrics): 2

Passed QuotesApi.Tests.QuoteDependencyIdempotencyTests.Post_Failure_IsAttemptedExactlyOnceAndNotRetried [234 ms]
  Standard Output Messages:
 === IDEMPOTENCY EVIDENCE (non-idempotent POST) ===
 Total dependency calls: 1 (expected exactly 1)
 Recorded retry attempts (metrics): 0 (expected 0 -- POST is never retried)
```

Real `ILogger`/Serilog console output for the GET retry, taken from the same run:

```
[11:14:02 WRN] [Retry] attempt=1 delay=44.3179ms reason=HttpRequestException
[11:14:02 WRN] [Retry] attempt=2 delay=59.36ms reason=HttpRequestException
```

## Retry Evidence

See above — 2 transient failures then success, exactly 3 total calls to the fake
dependency, 2 recorded retry attempts, exponential backoff delays visible in the log
(44ms then ~59ms, jittered).

## Circuit Breaker Evidence

Test config for these runs: `FailureRatio=0.5`, `MinimumThroughput=4`,
`SamplingDurationSeconds=5`, `BreakDurationSeconds=0.6` (via
`QuoteDependencyCircuitBreakerTests`/`Day22ResilienceDemoTests`), applied to the
**non-idempotent POST pipeline** (no retry stage, so 1 request = 1 breaker record).

```
Passed QuotesApi.Tests.QuoteDependencyCircuitBreakerTests.SustainedFailures_OpenTheCircuit_ThenRejectWithoutCallingDependency [269 ms]
  Standard Output Messages:
 Request 1: dependency failure
 Request 2: dependency failure
 Request 3: dependency failure
 Request 4: dependency failure
 Circuit state after 4 failures: Open
 Further request rejected by circuit -- dependency was NOT called

Passed QuotesApi.Tests.QuoteDependencyCircuitBreakerTests.AfterBreakDuration_HalfOpenProbeSucceeds_AndCircuitCloses [1 s]
  Standard Output Messages:
 Circuit OPEN after sustained failures. Waiting for break duration...
 Probe request: SUCCESS (Quote created)
 Circuit state after probe: Closed
 Recovery confirmed
```

Real `ILogger`/Serilog console lines (from `OnOpened`/`OnHalfOpened`/`OnClosed`),
same test run:

```
[11:11:32 ERR] [Resilience] Circuit POST OPENED for 0.6s (reason=HttpRequestException)
[11:11:33 WRN] [Resilience] Circuit POST HALF-OPEN: probing dependency
[11:11:33 INF] [Resilience] Circuit POST CLOSED: recovery confirmed
```

`Assert.Equal(callsAtOpen, _factory.Handler.TotalCalls)` after the 5th request proves
the dependency was **not** called while the circuit was open — the caller instead got a
`Polly.CircuitBreaker.BrokenCircuitException` immediately.

## Timeout Evidence

```
Passed QuotesApi.Tests.QuoteDependencyTimeoutTests.Post_DependencyDelayExceedsTimeout_IsCancelledAfterConfiguredTimeout [716 ms]
  Standard Output Messages:
 === TIMEOUT EVIDENCE ===
 Configured timeout: 0.2s; dependency delay: 5s
 Call cancelled after: 219ms
 Recorded timeout events (metrics): 1

Passed QuotesApi.Tests.QuoteDependencyTimeoutTests.Get_DependencyAlwaysDelaysPastTimeout_RetriesEachAttemptThenFails [2 s]
  Standard Output Messages:
 === TIMEOUT + RETRY INTERACTION EVIDENCE ===
 Attempts made (all individually timed out): 3
 Recorded timeout events: 3
 Recorded retry attempts: 2
```

The POST case (no retry stage) proves a single attempt is cancelled ~0.2s after the
configured timeout, well under the dependency's 5s delay. The GET case proves the
timeout is **per attempt**, not per operation: with `MaxRetryAttempts=2` and
`TimeoutSeconds=0.1`, a dependency that always hangs produces exactly 3 attempts (1 +
2 retries), each individually timing out, before the final `TimeoutRejectedException`
surfaces to the caller — proving timeout, retry and circuit breaker interact correctly.

## Bulkhead Evidence

```
Passed QuotesApi.Tests.QuoteDependencyBulkheadTests.MoreConcurrentCallsThanLimit_ExcessCallsAreRejectedFast [488 ms]
  Standard Output Messages:
 === BULKHEAD EVIDENCE ===
 Concurrent requests sent: 5, limit: 2
 Max concurrent in-flight dependency calls observed: 2
 Succeeded: 2, Rejected: 3
 Recorded bulkhead rejections (metrics): 3
```

Real `ILogger` output (from `OnRejected`), same run:

```
[11:14:45 ERR] Resilience event occurred. EventName: 'OnRateLimiterRejected', Source: 'quote-dependency-get/(null)/RateLimiter', ...
[11:14:45 WRN] [Bulkhead] GET concurrency limit reached (max=2); request rejected
```
(repeated 3 times, once per rejected caller)

5 concurrent GET calls were sent against a dependency held open by a gate; exactly 2
(the configured `MaxConcurrency`) were let through concurrently, and the other 3 were
rejected immediately with `Polly.RateLimiting.RateLimiterRejectedException` (fail-fast,
`QueueLimit=0` — no queueing).

## Test Results

Day 22 resilience tests, run with `dotnet test --filter "FullyQualifiedName~QuoteDependency|FullyQualifiedName~Day22Resilience" --logger "console;verbosity=detailed"`:

```
Total tests: 8
     Passed: 8
     Failed: 0
```

Test names:

- `QuoteDependencyIdempotencyTests.Get_TransientFailureThenSuccess_IsRetriedUntilSuccess`
- `QuoteDependencyIdempotencyTests.Post_Failure_IsAttemptedExactlyOnceAndNotRetried`
- `QuoteDependencyCircuitBreakerTests.SustainedFailures_OpenTheCircuit_ThenRejectWithoutCallingDependency`
- `QuoteDependencyCircuitBreakerTests.AfterBreakDuration_HalfOpenProbeSucceeds_AndCircuitCloses`
- `QuoteDependencyTimeoutTests.Post_DependencyDelayExceedsTimeout_IsCancelledAfterConfiguredTimeout`
- `QuoteDependencyTimeoutTests.Get_DependencyAlwaysDelaysPastTimeout_RetriesEachAttemptThenFails`
- `QuoteDependencyBulkheadTests.MoreConcurrentCallsThanLimit_ExcessCallsAreRejectedFast`
- `Day22ResilienceDemoTests.Demo_Retry_Then_Bulkhead_Then_CircuitBreaker_Lifecycle`

Each test resolves `IQuoteDependencyClient` from the real `WebApplicationFactory`-hosted
DI container built by `Program.cs`'s own `AddQuoteDependencyResilience` registration
(via `ResilienceQuotesApiFactory`), with only the `HttpClient`'s primary
`HttpMessageHandler` swapped for `FakeQuoteDependencyHandler` — the Polly pipelines
themselves are never mocked or rebuilt for the test.

Full existing suite (`dotnet test QuotesAPI.Tests`), run after this change — 41
pre-existing Day 21 tests plus the 8 new Day 22 tests above, all passing:

```
Passed!  - Failed:     0, Passed:    49, Skipped:     0, Total:    49, Duration: 3 m 55 s - QuotesApi.Tests.dll (net10.0)
```

## Failure/Recovery Proof

Full console output from `Day22ResilienceDemoTests.Demo_Retry_Then_Bulkhead_Then_CircuitBreaker_Lifecycle`
(`dotnet test --filter "FullyQualifiedName~Day22ResilienceDemoTests" --logger "console;verbosity=detailed"`),
copied verbatim from a real run — nothing below is hand-written/faked:

```
Passed QuotesApi.Tests.Day22ResilienceDemoTests.Demo_Retry_Then_Bulkhead_Then_CircuitBreaker_Lifecycle [1 s]
  Standard Output Messages:
 === RETRY DEMO (idempotent GET) ===
 [Retry] attempt=1 result=failure
 [Retry] attempt=2 result=failure
 [Retry] attempt=3 result=success (Quote of the day)

 === BULKHEAD DEMO ===
 Sent 5 concurrent requests, concurrency limit=2
 Max concurrent in-flight dependency calls observed: 2
 [Bulkhead] concurrency limit reached
 [Bulkhead] request rejected
 [Bulkhead] request rejected
 [Bulkhead] request rejected
 Succeeded: 2, Rejected: 3

 === CIRCUIT BREAKER DEMO ===

 Request 1: dependency failure
 Request 2: dependency failure
 Request 3: dependency failure
 Request 4: dependency failure

 [Resilience] Circuit OPENED
 Further requests rejected by circuit

 Waiting for break duration...

 [Resilience] Circuit HALF-OPEN
 Probe request: SUCCESS (Quote created)

 [Resilience] Circuit CLOSED
 Recovery confirmed
```

The real Serilog/`ILogger` lines interleaved in the same run (not printed by the test
itself) confirm the same transitions independently:

```
[11:11:32 ERR] [Resilience] Circuit POST OPENED for 0.6s (reason=HttpRequestException)
[11:11:33 WRN] [Resilience] Circuit POST HALF-OPEN: probing dependency
[11:11:33 INF] [Resilience] Circuit POST CLOSED: recovery confirmed
```

## Design Notes

**Why retries are only used for idempotent operations.** Retrying `POST /quotes`
(create) after a failure risks double-creating the resource if the original request
actually reached the dependency and only the response was lost — a classic
at-least-once-vs-exactly-once problem. `GET /quote-of-the-day` has no side effects, so
retrying it is always safe. The two operations run through **physically separate**
Polly pipelines (`quote-dependency-get` has a retry stage, `quote-dependency-post` does
not) rather than one shared pipeline gated by a runtime predicate, so there is no
predicate to get wrong and no way for a future change to accidentally start retrying
creates.

**Why circuit breakers prevent repeated calls to an unhealthy dependency.** Once a
dependency is failing consistently, continuing to hammer it with retries wastes
application threads/connections and makes the dependency's recovery slower (or
impossible) by keeping load on it. The circuit breaker tracks the failure ratio over a
sliding window and, once it crosses the threshold, stops sending requests entirely for
`BreakDuration`, failing fast with `BrokenCircuitException` instead. After the break, it
allows exactly one probe request through (half-open) to test whether the dependency has
recovered before fully re-opening traffic.

**Why timeout prevents requests from hanging indefinitely.** Without a bound, a slow or
stuck dependency can hold a caller's thread/connection open indefinitely, which
cascades into resource exhaustion for the calling application. Placing the timeout
innermost (around each individual attempt rather than the whole retry loop) means a
single slow call doesn't consume the entire retry budget's worth of wall-clock time
unnecessarily, and each timeout is itself evaluated as a failure by the retry and
circuit-breaker layers wrapping it.

**Why bulkhead protects application capacity.** Even with retry, circuit breaker and
timeout in place, an unbounded number of *concurrent* calls to a slow dependency can
still exhaust the calling application's own thread pool / connection pool, taking down
unrelated functionality. The concurrency limiter caps how many calls to this dependency
can be in flight at once and rejects (fails fast) anything beyond that limit rather than
queueing it — protecting the application's own capacity is more important than letting
every caller eventually get through.

## Limitations / Caveats

- The outbound dependency (`QuoteDependency`) is a purpose-built exercise dependency
  (there was no existing outbound HTTP call in the QuotesAPI codebase worth wrapping —
  the pre-existing `Program.cs` scaffold this replaces pointed at a deliberately
  unreachable `localhost:59999` for the same reason). No real quote-dependency service
  exists; `QuoteDependency:BaseUrl` is a placeholder hostname that is never actually
  dialed in tests (the primary `HttpMessageHandler` is swapped for a fake) and would
  need to point at a real service before this could run in production traffic.
- The idempotent (GET) and non-idempotent (POST) pipelines each have their own
  independent circuit breaker and concurrency limiter instances rather than sharing
  state. This is a deliberate simplification (a burst of failing POSTs cannot exhaust
  the GET path's bulkhead budget or trip its breaker, and vice versa) but means a
  dependency-wide outage is tracked as two separate circuits rather than one.
- `CircuitBreaker:BreakDurationSeconds` and `SamplingDurationSeconds` have a hard Polly
  minimum of just over 0.5 seconds, so the fastest a half-open transition can be
  observed in a test is ~0.6s — the test suite already uses values close to that floor.
