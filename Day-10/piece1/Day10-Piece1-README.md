# Day 10 — Piece 1: EF Core Change Tracking vs AsNoTracking

## Overview

This exercise demonstrates the behavior and overhead of Entity Framework Core change tracking by comparing a standard tracked query with an `AsNoTracking()` query.

The experiment was implemented in the existing **QuotesAPI** project using its existing **SQLite** database.

The same 10,000 active `Quote` records were queried using both approaches and the following were measured:

- Number of rows returned
- Execution time
- Allocated memory
- Number of entities tracked by EF Core

## Objective

The objectives of this exercise are to:

1. Understand how EF Core change tracking works.
2. Demonstrate the difference between tracked and non-tracked queries.
3. Compare a normal EF Core query with `AsNoTracking()`.
4. Measure the performance and memory impact of change tracking.
5. Verify the number of entities stored in the `ChangeTracker`.
6. Identify when tracking is required and when `AsNoTracking()` is appropriate.

## Environment

| Component | Details |
|---|---|
| Framework | .NET 10 |
| ORM | Entity Framework Core |
| Database | SQLite |
| Application | QuotesAPI |
| Entity | `Quote` |
| Dataset | 10,000 active quotes |

The experiment was added to the existing application rather than creating a separate project or database.

---

## 1. EF Core Change Tracking

EF Core tracks entities returned from queries by default.

When an entity is tracked, the `DbContext` maintains information about its state. This allows EF Core to detect changes and persist them when `SaveChanges()` or `SaveChangesAsync()` is called.

### Tracking Query

```csharp
var trackedQuotes = await db.Quotes
    .Where(q => !q.IsDeleted)
    .Take(10000)
    .ToListAsync();
```

No tracking option is specified here, so EF Core uses its default tracking behavior.

After the query executes, the returned `Quote` entities are tracked by the current `DbContext`.

---

## 2. AsNoTracking

`AsNoTracking()` disables change tracking for a specific query.

### No-Tracking Query

```csharp
var noTrackingQuotes = await db.Quotes
    .AsNoTracking()
    .Where(q => !q.IsDeleted)
    .Take(10000)
    .ToListAsync();
```

The entities are still materialized and returned to the application, but they are not added to the `DbContext`'s `ChangeTracker`.

This makes `AsNoTracking()` useful for read-only operations.

---

## 3. Test Endpoint

A development-only endpoint was added to execute both query variants:

```text
GET /test-change-tracking
```

The application was started with:

```powershell
dotnet run
```

The endpoint was accessed using:

```text
http://localhost:5228/test-change-tracking
```

The test uses the same `DbContext` and clears the `ChangeTracker` between measurements.

---

## 4. Measurement Approach

Execution time was measured using `Stopwatch`.

Memory allocation was measured using:

```csharp
GC.GetTotalAllocatedBytes(true)
```

The number of tracked entities was verified using:

```csharp
db.ChangeTracker
    .Entries<Quote>()
    .Count();
```

Before each measurement, the Change Tracker was cleared:

```csharp
db.ChangeTracker.Clear();
```

This prevents entities from the previous test from affecting the next result.

---

## 5. Tracking Test

The tracking query was executed as follows:

```csharp
db.ChangeTracker.Clear();

var trackingStart =
    GC.GetTotalAllocatedBytes(true);

var trackingWatch =
    Stopwatch.StartNew();

var trackedQuotes =
    await db.Quotes
        .Where(q => !q.IsDeleted)
        .Take(10000)
        .ToListAsync();

trackingWatch.Stop();

var trackingEnd =
    GC.GetTotalAllocatedBytes(true);

var trackedEntities =
    db.ChangeTracker
        .Entries<Quote>()
        .Count();
```

The query returned 10,000 records.

The Change Tracker contained 10,000 `Quote` entities after the query.

---

## 6. AsNoTracking Test

The no-tracking query was executed as follows:

```csharp
db.ChangeTracker.Clear();

var noTrackingStart =
    GC.GetTotalAllocatedBytes(true);

var noTrackingWatch =
    Stopwatch.StartNew();

var noTrackingQuotes =
    await db.Quotes
        .AsNoTracking()
        .Where(q => !q.IsDeleted)
        .Take(10000)
        .ToListAsync();

noTrackingWatch.Stop();

var noTrackingEnd =
    GC.GetTotalAllocatedBytes(true);

var noTrackingEntities =
    db.ChangeTracker
        .Entries<Quote>()
        .Count();
```

The query returned the same 10,000 records.

The Change Tracker contained zero `Quote` entities after the query.

---

## 7. Test Results

The final test produced the following results:

| Metric | Tracking | `AsNoTracking()` |
|---|---:|---:|
| Rows returned | 10,000 | 10,000 |
| Execution time | 166 ms | 78 ms |
| Allocated memory | 10.91 MB | 4.61 MB |
| Tracked entities | 10,000 | 0 |

### Raw Response

```json
{
  "rowsRequested": 10000,
  "tracking": {
    "rows": 10000,
    "timeMs": 166,
    "allocatedMb": 10.91,
    "trackedEntities": 10000
  },
  "asNoTracking": {
    "rows": 10000,
    "timeMs": 78,
    "allocatedMb": 4.61,
    "trackedEntities": 0
  }
}
```

---

## 8. Performance Analysis

### Execution Time

The tracking query took:

```text
166 ms
```

The `AsNoTracking()` query took:

```text
78 ms
```

Difference:

```text
166 ms - 78 ms = 88 ms
```

In this run, `AsNoTracking()` reduced the measured execution time by approximately **53%**.

### Memory Allocation

The tracking query allocated:

```text
10.91 MB
```

The `AsNoTracking()` query allocated:

```text
4.61 MB
```

Difference:

```text
10.91 MB - 4.61 MB = 6.30 MB
```

In this run, `AsNoTracking()` used approximately **58% less allocated memory**.

> These measurements were collected from a local development run. They demonstrate the overhead difference for this workload and should not be treated as production benchmark numbers.

---

## 9. Change Tracker Verification

The experiment directly verified EF Core's tracking behavior.

The following code was used:

```csharp
db.ChangeTracker
    .Entries<Quote>()
    .Count();
```

### Tracking Query

Result:

```text
10000
```

All 10,000 materialized `Quote` entities were tracked.

### AsNoTracking Query

Result:

```text
0
```

No returned entities were tracked.

This confirms the behavioral difference between the two query modes.

---

## 10. Tracking vs AsNoTracking

| Feature | Tracking | `AsNoTracking()` |
|---|---|---|
| Default EF Core query behavior | Yes | No |
| Adds entities to Change Tracker | Yes | No |
| Change detection | Available | Not automatic |
| Suitable for read-only queries | Yes | Yes |
| Suitable for update workflows | Yes | Requires explicit state handling |
| Tracking memory overhead | Higher | Lower |
| Tracking processing overhead | Higher | Lower |
| Useful for large read-only result sets | Usually unnecessary | Appropriate when no tracking is required |

---

## 11. When to Use AsNoTracking

`AsNoTracking()` is appropriate when the query is genuinely read-only.

Typical examples include:

- Read-only API endpoints
- Search results
- Reporting queries
- Dashboard data
- Read-only lists
- Read-only detail pages
- Data exports

Example:

```csharp
var quotes = await db.Quotes
    .AsNoTracking()
    .Where(q => !q.IsDeleted)
    .ToListAsync();
```

If the application only needs to read and return the data, maintaining tracking state is often unnecessary.

---

## 12. When Not to Use AsNoTracking

Do not use `AsNoTracking()` when the retrieved entity is expected to be modified and persisted through the same `DbContext` without explicitly handling its state.

For example:

```csharp
var quote = await db.Quotes
    .FirstAsync(q => q.Id == id);

quote.SoftDelete();

await db.SaveChangesAsync();
```

With normal tracking, EF Core can detect the modification and persist it.

With `AsNoTracking()`, the entity is not automatically tracked by the `DbContext`, so explicit state management would be required before saving changes.

The choice should therefore be based on the intended use of the entity, not simply on which query is faster.

---

## 13. Identity Resolution

EF Core tracking also provides identity resolution within a `DbContext`.

When the same entity is materialized multiple times in a tracking query, EF Core can ensure that the same database entity is represented by the same in-memory object instance.

A standard `AsNoTracking()` query does not provide this identity resolution behavior.

When no normal tracking is required but identity resolution is still needed, EF Core provides:

```csharp
.AsNoTrackingWithIdentityResolution()
```

This should be used only when the query specifically requires identity resolution without normal `DbContext` tracking.

---

## 14. Key Findings

### Finding 1 — Tracking maintains entity state

The tracking query returned:

```text
10,000 rows
10,000 tracked entities
```

EF Core therefore maintained tracking information for every returned entity.

### Finding 2 — AsNoTracking avoids tracking

The no-tracking query returned:

```text
10,000 rows
0 tracked entities
```

The result set was unchanged, but the tracking behavior was different.

### Finding 3 — Tracking introduced measurable overhead

For this test:

```text
Tracking
166 ms
10.91 MB
```

compared with:

```text
AsNoTracking()
78 ms
4.61 MB
```

The no-tracking query showed lower execution time and memory allocation.

### Finding 4 — The use case determines the correct approach

The goal should not be to always use `AsNoTracking()`.

Instead:

```text
Entity will be modified and saved
            |
            v
        Tracking

Entity is only being read
            |
            v
      AsNoTracking()
```

---

## 15. Limitations

This exercise is intended to demonstrate EF Core behavior rather than provide a formal performance benchmark.

The measured values can vary based on:

- Machine hardware
- Database state
- SQLite caching
- JIT compilation
- .NET runtime state
- Background processes
- Query shape
- Dataset size

For production benchmarking, multiple iterations and a dedicated benchmarking tool such as BenchmarkDotNet would provide more reliable measurements.

---

## 16. How to Run

Navigate to the project:

```powershell
cd Day-10\piece1\QuotesAPI
```

Run the application:

```powershell
dotnet run
```

Open the test endpoint:

```text
http://localhost:5228/test-change-tracking
```

The endpoint executes both query variants and returns:

- Rows returned
- Execution time
- Allocated memory
- Tracked entity count

---

## 17. Expected Verification

A successful run should demonstrate:

```text
Tracking
    Rows: 10000
    Tracked entities: 10000

AsNoTracking
    Rows: 10000
    Tracked entities: 0
```

The exact execution time and memory allocation may vary between runs.

---

## 18. Conclusion

This exercise demonstrated that EF Core's default tracking behavior and `AsNoTracking()` serve different purposes.

Tracking is valuable when entities need to participate in an update workflow because EF Core can detect changes and persist them.

For read-only operations, `AsNoTracking()` avoids maintaining unnecessary tracking state.

In the measured 10,000-row test:

```text
Tracking
166 ms
10.91 MB
10,000 tracked entities
```

compared with:

```text
AsNoTracking()
78 ms
4.61 MB
0 tracked entities
```

The result demonstrates a practical EF Core optimization:

> Use `AsNoTracking()` for appropriate read-only query paths, while retaining normal tracking when entities need to be modified and persisted through the current `DbContext`.
