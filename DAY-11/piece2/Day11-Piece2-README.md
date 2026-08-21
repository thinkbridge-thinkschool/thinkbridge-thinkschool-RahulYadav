# Day 11 — Piece 2: Fix the Slow Endpoint

## Overview

This task fixes the database performance problems identified in Day 11 — Piece 1.

The original endpoint demonstrated:

1. An **N+1 query pattern** when loading quotes for each author.
2. A **missing index** for the author-based quote lookup.

Piece 2 removes the N+1 pattern, adds a composite index, and re-runs the endpoint using the same k6 configuration.

---

## Objective

- Eliminate the N+1 query pattern.
- Replace repeated database calls with a single EF Core query.
- Add the appropriate database index.
- Apply the index through an EF Core migration.
- Re-measure using the same k6 configuration.
- Capture before and after execution plans.
- Compare before and after p99 latency.
- Verify the required **≥10× improvement**.

---

# 1. N+1 Problem

The original implementation first retrieved distinct authors:

```csharp
var authors = await db.Quotes
    .Where(q => !q.IsDeleted)
    .Select(q => q.Author)
    .Distinct()
    .ToListAsync(cancellationToken);
```

It then queried quotes separately for every author:

```csharp
foreach (var author in authors)
{
    var quotes = await db.Quotes
        .Where(q =>
            !q.IsDeleted &&
            q.Author == author)
        .Select(q => new
        {
            q.Id,
            q.Author,
            q.Text
        })
        .ToListAsync(cancellationToken);
}
```

With approximately 100 authors, this resulted in approximately:

```text
1 query   → retrieve authors
100 queries → retrieve quotes
-------------------------------
≈ 101 database queries
```

---

# 2. N+1 Fix

The endpoint was changed to use one EF Core query:

```csharp
var result = await db.Quotes
    .Where(q => !q.IsDeleted)
    .GroupBy(q => q.Author)
    .Select(g => new
    {
        Author = g.Key,
        Quotes = g.Select(q => new
        {
            q.Id,
            q.Author,
            q.Text
        }).ToList()
    })
    .ToListAsync(cancellationToken);
```

The application no longer executes a query inside a loop.

### Before

```text
Get authors
    ↓
Query quotes for Author 1
    ↓
Query quotes for Author 2
    ↓
Query quotes for Author 3
    ↓
...
```

### After

```text
One EF Core query
        ↓
Database performs the query
        ↓
Results returned
```

---

# 3. Index Added

The performance query filters using `Author` and `IsDeleted`.

A composite index was added:

```csharp
modelBuilder.Entity<Quote>()
    .HasIndex(q => new
    {
        q.Author,
        q.IsDeleted
    })
    .HasDatabaseName("IX_Quotes_Author_IsDeleted");
```

The resulting SQL index is:

```sql
CREATE INDEX "IX_Quotes_Author_IsDeleted"
ON "Quotes" ("Author", "IsDeleted");
```

This index supports the author lookup together with the deleted-row filter.

---

# 4. EF Core Migration

Migration created:

```powershell
dotnet ef migrations add AddAuthorIsDeletedIndex
```

Migration applied with:

```powershell
dotnet ef database update
```

EF Core successfully generated:

```sql
CREATE INDEX "IX_Quotes_Author_IsDeleted"
ON "Quotes" ("Author", "IsDeleted");
```

---

# 5. Before Execution Plan

The Piece 1 query plan was:

```text
SCAN q
```

The query used for the plan was:

```sql
EXPLAIN QUERY PLAN
SELECT "q"."Id",
       "q"."Author",
       "q"."Text"
FROM "Quotes" AS "q"
WHERE NOT ("q"."IsDeleted")
  AND "q"."Author" = 'Performance Author 1';
```

The baseline endpoint returned:

```json
[
  {
    "id": 2,
    "parent": 0,
    "notUsed": 216,
    "detail": "SCAN q"
  }
]
```

`SCAN q` showed that SQLite was scanning the `Quotes` table rather than using an index.

---

# 6. After Execution Plan

After adding the composite index, the same query-plan endpoint returned:

```json
[
  {
    "id": 3,
    "parent": 0,
    "notUsed": 62,
    "detail": "SEARCH q USING INDEX IX_Quotes_Author_IsDeleted (Author=?)"
  }
]
```

### Before

```text
SCAN q
```

### After

```text
SEARCH q USING INDEX IX_Quotes_Author_IsDeleted (Author=?)
```

This confirms that SQLite is using the new index for the author lookup.

---

# 7. Load Test

The same k6 configuration was used for the after measurement.

```text
Virtual Users: 10
Duration:      30 seconds
```

Endpoint:

```text
http://localhost:5228/api/performance/author-quotes
```

The load-test configuration was not changed.

Command:

```powershell
k6 run .\load-test.js
```

---

# 8. Before Performance

The Piece 1 baseline was:

| Metric | Before |
|---|---:|
| p50 | **2.00 s** |
| p90 | 2.30 s |
| p95 | 2.74 s |
| p99 | **2.75 s** |
| Average | 2.09 s |
| Maximum | 3.16 s |

Important baseline:

```text
Before p99 = 2.75 seconds
            = 2750 ms
```

---

# 9. After Performance

The same 10 VU / 30 second configuration was executed after the fixes.

Measured results:

```text
p50 = 22.52 ms
p99 = 79 ms
```

The k6 test completed successfully:

```text
Requests:        11,922
Failed requests: 0%
Virtual Users:   10
Duration:        30 seconds
```

Therefore:

```text
After p99 = 79 ms
```

---

# 10. p99 Improvement

Before:

```text
2750 ms
```

After:

```text
79 ms
```

Calculation:

```text
2750 / 79 = 34.81×
```

### Result

**Approximately 34.8× p99 improvement.**

Required target:

```text
≥ 10× improvement
```

Achieved:

```text
≈ 34.8× improvement
```

**Target exceeded.**

---

# 11. Before vs After

| Metric | Before | After |
|---|---:|---:|
| p99 | **2.75 s** | **79 ms** |
| Execution plan | `SCAN q` | `SEARCH q USING INDEX IX_Quotes_Author_IsDeleted (Author=?)` |
| N+1 | Present | Eliminated |
| Author lookup index | Missing | Added |
| Index used | No | Yes |

### p99 improvement

```text
2750 ms / 79 ms = 34.81×
```

**Final improvement: approximately 34.8×.**

---

# 12. Benchmark Note

Both measurements used the same load configuration:

```text
10 VUs
30 seconds
```

However, the total completed request counts differed:

```text
Piece 1:  approximately 150 requests
Piece 2:  11,922 requests
```

Therefore, the request counts should not be presented as identical. The primary comparison is the measured p99 under the same configured VU/duration load, with the difference in completed requests documented for transparency.

---

# 13. Changes Made

## Change 1 — Eliminated N+1

The repeated per-author queries were replaced with a single EF Core query using grouping and projection.

```text
Before: approximately 101 database queries
After:  single EF Core query
```

## Change 2 — Added Composite Index

```text
IX_Quotes_Author_IsDeleted
```

Definition:

```csharp
modelBuilder.Entity<Quote>()
    .HasIndex(q => new
    {
        q.Author,
        q.IsDeleted
    })
    .HasDatabaseName("IX_Quotes_Author_IsDeleted");
```

## Change 3 — Verified Query Plan

Before:

```text
SCAN q
```

After:

```text
SEARCH q USING INDEX IX_Quotes_Author_IsDeleted (Author=?)
```

---

# 14. Reproduction

Navigate to the project:

```powershell
cd D:\Desktop\ThinkSchool\DAY-11\piece2\QuotesAPI
```

Apply migrations:

```powershell
dotnet ef database update
```

Start the API:

```powershell
dotnet run
```

Check the execution plan:

```text
http://localhost:5228/api/performance/query-plan
```

Run the load test from another terminal:

```powershell
k6 run .\load-test.js
```

---

# 15. Final Evidence

### Before p99

```text
2.75 s
```

### After p99

```text
79 ms
```

### p99 improvement

```text
34.81×
```

### Before plan

```text
SCAN q
```

### After plan

```text
SEARCH q USING INDEX IX_Quotes_Author_IsDeleted (Author=?)
```

### N+1

```text
Before: approximately 101 database queries
After:  single EF Core query
```

### Index

```text
IX_Quotes_Author_IsDeleted
```

---

# Key Learning

A slow API endpoint should be investigated using measurable database evidence rather than only application response time.

The baseline identified two problems: an N+1 query pattern and a missing index.

The N+1 pattern was eliminated by replacing repeated database calls with a single EF Core query. A composite index was then added for the author and `IsDeleted` lookup.

The execution plan changed from:

```text
SCAN q
```

to:

```text
SEARCH q USING INDEX IX_Quotes_Author_IsDeleted (Author=?)
```

Under the same **10 VUs / 30 seconds** k6 configuration, p99 decreased from **2.75 seconds to 79 ms**, representing approximately a **34.8× improvement**.

---

# Final Result

```text
N+1 queries
      ↓
Single EF Core query

Missing index
      ↓
IX_Quotes_Author_IsDeleted

SCAN q
      ↓
SEARCH q USING INDEX IX_Quotes_Author_IsDeleted

p99: 2.75 s
      ↓
p99: 79 ms

Improvement: ≈ 34.8×
```

**Target:** ≥10× improvement

**Achieved:** ≈34.8× improvement

**Status: PASS**
