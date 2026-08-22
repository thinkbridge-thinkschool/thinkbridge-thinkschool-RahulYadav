# Day 12 — Piece 2: EF Core vs Dapper

## Objective

Keep EF Core as the default data-access approach and evaluate whether Dapper earns its place on a hot read path.

For the Quotes API, the same paginated quote-read query was implemented using:

- EF Core with LINQ and `AsNoTracking()`
- Dapper with explicit SQL

The two implementations were compared using the same endpoint parameters and local benchmark conditions.

## Read Query

The query retrieves paginated quotes ordered by ID.

Parameters used:

```text
page = 1
size = 100
```

## EF Core Implementation

```csharp
var query = _db.Quotes
    .AsNoTracking()
    .OrderBy(q => q.Id)
    .Skip((request.Page - 1) * request.Size)
    .Take(request.Size)
    .Select(q => new QuoteReadModel
    {
        Id = q.Id,
        Author = q.Author,
        Text = q.Text
    });

return await query.ToListAsync(cancellationToken);
```

### EF Core SQL

```sql
SELECT "q"."Id", "q"."Author", "q"."Text"
FROM "Quotes" AS "q"
ORDER BY "q"."Id"
LIMIT @p1 OFFSET @p
```

## Dapper Implementation

```csharp
const string sql = """
    SELECT
        Id,
        Author,
        Text
    FROM Quotes
    ORDER BY Id
    LIMIT @Size
    OFFSET @Offset;
    """;

var offset = (request.Page - 1) * request.Size;

var command = new CommandDefinition(
    sql,
    new
    {
        Size = request.Size,
        Offset = offset
    },
    cancellationToken: cancellationToken);

var quotes = await connection.QueryAsync<QuoteReadModel>(command);

return quotes.ToList();
```

### Dapper SQL

```sql
SELECT
    Id,
    Author,
    Text
FROM Quotes
ORDER BY Id
LIMIT @Size
OFFSET @Offset;
```

## Endpoint Comparison

### EF Core

```text
GET /api/quotes/?page=1&size=100
```

```text
HTTP Request
    ↓
GetQuotesQuery
    ↓
GetQuotesQueryHandler
    ↓
EF Core LINQ
    ↓
QuoteReadModel
```

### Dapper

```text
GET /api/quotes/dapper?page=1&size=100
```

```text
HTTP Request
    ↓
GetQuotesDapperQuery
    ↓
GetQuotesDapperQueryHandler
    ↓
Dapper + SQL
    ↓
QuoteReadModel
```

Both implementations return the same read-model shape.

## Timing Comparison

Five measurements were taken for each implementation after warming up the endpoints.

| Run | EF Core | Dapper |
|---|---:|---:|
| 1 | 25.9661 ms | 16.5060 ms |
| 2 | 10.7484 ms | 2.6770 ms |
| 3 | 9.3842 ms | 3.7567 ms |
| 4 | 11.6166 ms | 4.7356 ms |
| 5 | 15.7056 ms | 3.0836 ms |
| **Average** | **14.68 ms** | **6.15 ms** |

Dapper averaged approximately **6.15 ms**, compared with **14.68 ms** for EF Core.

In this local benchmark, Dapper was approximately **58% faster** based on average elapsed time.

This result is specific to this local test environment and workload and should not be treated as a universal performance claim.

## Rule for EF Core vs Dapper

**EF Core should remain the default because it provides strong typing, LINQ support, and easier maintenance. Dapper should be introduced only when profiling shows that a specific high-traffic read path is a meaningful performance bottleneck and a stable SQL query can benefit from lower data-access overhead. The decision should be based on measured performance under realistic workload conditions rather than assuming Dapper is always faster.**

## Validation

### Build

The project successfully built with:

```powershell
dotnet build
```

Result:

```text
Build succeeded
```

There is an existing `NU1903` warning for `SQLitePCLRaw.lib.e_sqlite3`; it did not prevent the application from building or running.

### Application

The API successfully started on:

```text
http://localhost:5228
```

### Read Tests

Both endpoints were tested with the same parameters:

```text
GET /api/quotes/?page=1&size=100
GET /api/quotes/dapper?page=1&size=100
```

## Key Learning

Dapper is not automatically a replacement for EF Core. EF Core remains a good default, while Dapper is useful when a measured hot read path needs tighter SQL control or lower data-access overhead.

## Scope

This exercise only compares two read implementations.

There is:

- No change to the write/command path.
- No event sourcing.
- No separate database.
- No caching layer.
- No claim that Dapper is universally faster.

The purpose is to use profiling and measurement to decide whether introducing Dapper is justified.
