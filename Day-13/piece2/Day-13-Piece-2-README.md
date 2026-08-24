# Day 13 — Piece 2: Quotes List + Detail

## Overview

Extended the Angular 21 standalone QuotesAPI frontend from Piece 1 to support both a quotes list and quote detail view using the real Week-1 API.

## Real API

### List
`GET /api/quotes/?page={page}&size={size}`

### Detail
`GET /api/quotes/{id}`

### Quote Fields
- `id`
- `author`
- `text`
- `isDeleted`

The API uses camelCase JSON responses.

## Implementation

- Angular 21 standalone components
- No NgModules
- Typed `Quote` model — no `any`
- `QuoteService` uses `inject(HttpClient)`
- Quote list with `@for` and `track quote.id`
- Quote selection using Angular `input()` / `output()`
- Quote detail component
- Signals for loading, error and quote data
- `@if` / `@switch` for UI states
- `switchMap()` for stale-response protection
- Existing zoneless Angular configuration retained

## States Tested

- **Loading:** verified while the detail API request is in progress.
- **Data:** verified real quote details are displayed.
- **Empty:** verified when no quote is selected.
- **Error:** verified 404 and 500 responses.
- **Race condition:** selected one quote and quickly selected another; the previous request was cancelled so a stale response could not overwrite the newer selection.
- **List:** existing loading, error, empty and data states from Piece 1 were retained and verified.

## Bug Found and Fixed

The first race-condition test incorrectly assumed that an earlier HTTP request could still be flushed after a newer quote was selected.

Because `switchMap()` unsubscribes from the previous HTTP observable, the earlier request was already cancelled. The test failed with a cancelled-request error.

The test was corrected to verify:

```typescript
expect(reqA.cancelled).toBe(true);
```

This confirms that the stale request is cancelled at the HTTP layer rather than allowing an outdated response to overwrite the current detail.

## Verification

- `dotnet build` — Passed
- `ng build` — Passed
- `ng test` — **11/11 passed**
- Real list endpoint — Passed
- Real detail endpoint — Passed
- Real 404 detail request — Passed
- Invalid page request — Passed
- Angular dev-server proxy verification — Passed
- Loading/error/empty states — Verified
- Stale-response race — Verified

## What Would Break If the API Contract Changes?

If the API endpoint, query parameters, response structure, or fields such as `id`, `author`, or `text` change, the Angular model, service and components would need to be updated.

If the detail endpoint changes its 404 behavior, the detail error-handling logic would also need to be updated.

## Run Locally

### Backend

```bash
dotnet run --urls http://localhost:5228
```

### Frontend

```bash
npm install
ng serve
```

Open:

`http://localhost:4200`

The Angular development proxy forwards `/api` requests to the QuotesAPI running on port `5228`.
