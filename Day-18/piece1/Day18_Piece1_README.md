# Day 18 Piece 1 — BackgroundService, Queue, and Graceful Shutdown

## Objective

Move slow work off the HTTP request thread by introducing a queue drained by a .NET `BackgroundService`.

This exercise demonstrates asynchronous background processing, a bounded queue, graceful shutdown with `CancellationToken`, error handling, backpressure, and the difference between `BackgroundService`, `IHostedService`, and Hangfire.

## Background Processing Flow

```text
HTTP Request
    |
    | enqueue quote ID
    v
Bounded Work Queue
    |
    v
QuoteProcessingBackgroundService
    |
    v
Slow Quote Processing
```

The request path enqueues work instead of waiting for the slow operation.

## BackgroundService

The worker is `QuoteProcessingBackgroundService`, derived from `BackgroundService`.

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    _logger.LogInformation(
        "Quote processing background worker starting.");

    try
    {
        await foreach (var quoteId in
            _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var repository =
                    scope.ServiceProvider
                        .GetRequiredService<IQuoteRepository>();

                var quote =
                    await repository.GetByIdAsync(
                        quoteId,
                        stoppingToken);

                if (quote is null)
                    continue;

                await Task.Delay(
                    _simulatedWorkDelay,
                    stoppingToken);

                // Slow processing happens here,
                // outside the HTTP request path.
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Background processing failed for quote {QuoteId}",
                    quoteId);
            }
        }
    }
    catch (OperationCanceledException)
        when (stoppingToken.IsCancellationRequested)
    {
        _logger.LogInformation(
            "Quote processing background worker cancellation requested; exiting the queue read loop.");
    }

    _logger.LogInformation(
        "Quote processing background worker stopped.");
}
```

The worker uses `async/await` and does not use blocking calls such as `Thread.Sleep()`, `.Wait()`, or `.Result`.

## Queue

The implementation uses a bounded asynchronous `Channel<int>`.

The quote ID is the queued work item.

The bounded queue provides backpressure and prevents unlimited in-memory work accumulation.

## Graceful Shutdown

The ASP.NET Core host cancels the `stoppingToken` during application shutdown.

```text
Application shutdown requested
        |
        v
Host cancels stoppingToken
        |
        v
BackgroundService observes cancellation
        |
        v
Queue read exits
        |
        v
ExecuteAsync completes
        |
        v
Worker stops cleanly
```

The cancellation token is passed through asynchronous operations so the worker does not remain blocked indefinitely.

Actual shutdown logs verified:

```text
Application is shutting down...
Quote processing background worker cancellation requested; exiting the queue read loop.
Quote processing background worker stopped.
```

Normal cancellation is not treated as an application failure.

## Slow Work Off the Request Thread

The request path enqueues the work rather than directly waiting for the slow operation.

```text
Request
   |
   v
QueueAsync(quoteId)
   |
   v
Return response

BackgroundService
   |
   v
Process queued work
```

This keeps slow processing outside the HTTP request path.

## Error Handling

Individual background work-item failures are caught and logged so one failed item does not unexpectedly terminate the worker.

Cancellation is handled separately from normal exceptions.

## Backpressure

The queue is bounded. If producers add work faster than the worker can process it, the queue cannot grow indefinitely.

This is an in-process queue and does not provide durable job storage.

## BackgroundService vs IHostedService vs Hangfire

### BackgroundService

`BackgroundService` is a convenient base class for long-running background processing hosted by ASP.NET Core. It is appropriate here because the feature continuously drains an in-process queue.

### IHostedService

`IHostedService` is the lower-level hosted-service abstraction with `StartAsync()` and `StopAsync()`. It is useful when custom startup/shutdown lifecycle behavior is required.

### Hangfire

Hangfire is better suited to durable background jobs and scheduled work when persistent storage, retries, delayed jobs, recurring jobs, visibility, or survival across process restarts are required.

## When Hangfire Over a Hosted Service?

**Use Hangfire when background work needs durable job persistence, retries, delayed/recurring scheduling, or visibility across process restarts; use a hosted service for simple in-process continuous work.**

## Verification Log

### Queue processing
Verified that work can be queued and consumed by the background worker.

### Multiple queued items
Verified multiple queued items are processed asynchronously.

### Slow work
Verified slow processing occurs in the `BackgroundService` instead of blocking the HTTP request path.

### Error handling
Verified an individual work-item failure is logged without unexpectedly terminating the worker.

### Cancellation
Verified the worker observes the host cancellation token and exits the queue-processing loop during shutdown.

### Graceful shutdown
Verified the actual shutdown logs:

```text
Application is shutting down...
Quote processing background worker cancellation requested; exiting the queue read loop.
Quote processing background worker stopped.
```

### Backpressure
Verified the queue is bounded.

## Concrete Issue Caught During Review

The shutdown test initially used an absolute timing assertion requiring shutdown to complete in less than five seconds.

Existing OpenTelemetry shutdown overhead could make that test fail even when the background worker itself stopped correctly.

The test was changed to compare shutdown behavior with and without a long in-flight delay instead of relying on an absolute five-second limit.

## Tests and Build

Final backend tests:

```text
21/21 passed
```

Backend build:

```text
Passed
```

The tests cover queueing, worker consumption, multiple queued items, error handling, cancellation, graceful shutdown, and existing API behavior.

## What Would Break This?

The implementation depends on the queue contract, dependency-injection registrations, repository behavior, and application shutdown lifecycle.

It could break if:

- The queued work-item type changes without updating the producer and consumer.
- The repository method used by the worker changes.
- The cancellation token stops being passed through asynchronous operations.
- The queue becomes unbounded and receives excessive work.
- The application requires durable jobs across restarts without persistent job storage.
- The slow operation depends on an API/database contract that changes without updating the worker and tests.

A `BackgroundService` queue is in-process. Work that must survive application restarts requires a durable job mechanism such as Hangfire or another persistent queue.

## Final Status

- [x] BackgroundService implemented
- [x] Bounded asynchronous queue implemented
- [x] Slow work moved off the request path
- [x] CancellationToken used
- [x] Graceful shutdown verified
- [x] Multiple queued items verified
- [x] Individual work-item errors handled
- [x] Backpressure supported
- [x] BackgroundService vs IHostedService vs Hangfire documented
- [x] Hangfire decision rule documented
- [x] Backend tests: 21/21 passed
- [x] Build passed
