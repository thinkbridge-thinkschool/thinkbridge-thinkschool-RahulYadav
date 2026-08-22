# Day 12 — Piece 1: CQRS-lite Read and Write Models

## Objective

Separate the read and write paths for the Quotes API without introducing event sourcing.

- **Write path:** Command → Command Handler → validation/domain model → database
- **Read path:** Query → Query Handler → projection → Read Model

The goal is to give writes and reads different shapes and responsibilities.

## Implementation

### Write Model

The write request is represented by `CreateQuoteCommand`.

It contains only the data required to create a quote:

- `Author`
- `Text`

### Command Handler

`CreateQuoteCommandHandler` handles the write operation.

Responsibilities:

1. Validate the author.
2. Validate the quote text.
3. Use the existing `Quote.Create()` domain factory.
4. Persist the created quote through EF Core.
5. Return the newly created quote ID.

The handler keeps validation and persistence concerns together on the command side.

### Read Model

`QuoteReadModel` represents the shape required by the API read response.

```csharp
public class QuoteReadModel
{
    public int Id { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
```

The read model is separate from the EF Core entity.

### Query Handler

`GetQuotesQueryHandler` handles the read operation.

The query uses:

- `AsNoTracking()` for a read-only operation.
- Pagination with `Skip()` and `Take()`.
- LINQ projection with `Select()`.
- `QuoteReadModel` as the result shape.

The important projection is:

```csharp
.Select(q => new QuoteReadModel
{
    Id = q.Id,
    Author = q.Author,
    Text = q.Text
})
```

This allows the read path to return only the fields required by the API response instead of exposing the persistence entity directly.

## Request Flow

### Write

```text
POST /api/quotes/
        |
        v
CreateQuoteCommand
        |
        v
CreateQuoteCommandHandler
        |
        +--> Validation
        |
        +--> Quote.Create()
        |
        +--> EF Core SaveChanges
        |
        v
Database
```

### Read

```text
GET /api/quotes/
        |
        v
GetQuotesQuery
        |
        v
GetQuotesQueryHandler
        |
        +--> AsNoTracking()
        |
        +--> Pagination
        |
        +--> LINQ Projection
        |
        v
QuoteReadModel
        |
        v
API Response
```

## Endpoint Changes

The existing quote endpoint group was kept in place.

### GET

`GET /api/quotes/?page=1&size=10`

Now uses the query/read-model path.

### POST

`POST /api/quotes/`

Now uses the command/write-model path while retaining the existing authorization requirement.

### Existing Endpoints

The existing single-quote GET and delete paths were left unchanged for this exercise.

## Validation and Testing

### Build

The project was successfully built using:

```powershell
dotnet build
```

Result:

```text
Build succeeded
```

There are existing package/SQLite warnings, but they did not prevent compilation or application startup.

### Application Startup

The API successfully started with:

```powershell
dotnet run
```

and listened on:

```text
http://localhost:5228
```

### Read Path Test

Request:

```text
GET /api/quotes/?page=1&size=10
```

Result:

```text
200 OK
```

The response returned the expected read-model shape:

```json
[
  {
    "id": 1,
    "author": "Test Author",
    "text": "Test quote"
  }
]
```

### Authorization Test

An unauthenticated POST request returned:

```text
401 Unauthorized
```

This is expected because the existing POST endpoint requires authorization.

## What Got Simpler?

> Separating commands from queries made the write path focused on validation and persistence, while the read path could directly project database data into the shape required by the API.

## Key Learning

Reads and writes do not always need the same data shape. Separating the two paths makes it easier to optimize read queries and keep write-side validation and persistence focused.

## Scope

This exercise uses a lightweight CQRS approach only.

There is:

- No event sourcing.
- No event store.
- No separate read database.
- No synchronization pipeline.

The command and query paths simply have separate responsibilities within the existing Quotes API.
