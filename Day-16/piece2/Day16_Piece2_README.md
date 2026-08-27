# Day 16 — Piece 2: State Management, Signals First

## Overview

This piece demonstrates feature-level state management using Angular Signals first.

The project was copied from the completed Day-16/piece1 project, so the existing routing, lazy loading, authentication guard, View Transitions, HttpClient, interceptors, and typed error handling were preserved.

Claude Code was directed to implement the state-management feature. I reviewed the generated changes, identified a real concurrency bug, had the agent fix it, and then verified the resulting behavior.

## Real Week-1 API Contract

The quotes list uses the real API endpoint:

```text
GET /api/quotes?page=N&size=N
```

The quote response uses the real fields:

```text
id
author
text
```

The existing `QuoteService` remains responsible for HTTP communication.

## What Was Implemented

A feature-specific signal state service was added:

```text
src/app/quote-list/quote-list-state.ts
```

The service owns:

```text
quotes
loading
error
page
pageSize
```

It exposes derived state using computed signals:

```text
isEmpty
errorMessage
hasNextPage
pageInfo
```

The `QuoteList` component consumes this service instead of maintaining duplicate loading, data, and error state.

## Signal State Flow

```text
QuoteList
   ↓
QuoteListState
   ↓
QuoteService
   ↓
GET /api/quotes?page=N&size=N
   ↓
Signals update
   ↓
QuoteList renders state
```

### Loading

When loading starts, `loading` becomes true and the previous error is cleared.

### Success

Successful API results are stored in the quotes signal and rendered using the real `id`, `author`, and `text` fields.

### Empty

An empty array is represented by a computed empty state rather than a separately stored boolean.

### Error

API failures are stored as the typed application error and exposed as a friendly error message. Loading is reset after failure.

## Concurrent Request Handling

A real concurrency issue was identified during review.

The previous implementation could allow an older HTTP request to finish after a newer request and overwrite the newer quote data.

For example:

```text
Page 1 request
       ↓
Page 2 request
       ↓
Page 2 response
       ↓
Page 1 response
```

The agent fixed this using an incrementing request ID. Only the latest request is allowed to update the state.

## Signals vs Signal Store / NgRx

Signals + a feature-specific service were chosen because the state is local to the quotes-list feature, has a small number of transitions, and is not shared by unrelated features.

### Decision Rule

I would consider Signal Store or NgRx when:

1. Multiple unrelated components/features need the same state.
2. State transitions become difficult to reason about.
3. Complex optimistic create/edit/delete and undo workflows are introduced.
4. Multiple effects/workflows need coordination.
5. Standardized state-management patterns are needed.
6. DevTools or time-travel debugging becomes valuable.

For the current quotes list, NgRx or Signal Store would add unnecessary complexity.

## Verification Log

### Loading

I verified the loading state while the quotes API request was in progress.

### Success

I verified successful loading from:

```text
GET /api/quotes?page=N&size=N
```

using the real fields:

```text
id
author
text
```

### Empty

I verified an empty API result:

```text
[]
```

and confirmed that the UI displayed the empty state.

### Error

I verified the real invalid-pagination case:

```text
GET /api/quotes/?page=0&size=5
```

The API returned its real `400` validation error and the UI displayed the friendly error state. Loading returned to false.

### Concurrent Updates

I exercised repeated pagination/page-size updates and verified that stale responses did not overwrite the latest state.

## Concrete Bug Caught

I caught a real concurrency bug in the original quote-list implementation.

An older HTTP request could finish after a newer request and overwrite the newer quote data.

I directed Claude Code to fix this using a request-ID guard. After the fix, only the latest request can update the signals.

The affected real API is:

```text
GET /api/quotes?page=N&size=N
```

with the quote fields:

```text
id
author
text
```

Tests were added to verify that stale responses and stale errors are ignored.

## What Breaks If the API Contract Changes?

The implementation depends on:

```text
GET /api/quotes?page=N&size=N
```

and:

```text
id
author
text
```

If the endpoint or pagination parameters change, the `QuoteService`, state service, and tests must be updated.

If `id`, `author`, or `text` changes, the Quote model, state service, templates, and tests must be updated.

If the API error format changes, the typed error mapping may also need to change.

## Test and Build Results

Final verification:

```text
Angular tests:       84/84 passed
Production build:    Passed
Backend tests:       Passed
```

The Angular tests cover initial state, loading, success, empty response, API error, loading reset, concurrent requests, pagination, and page-size changes.

I also manually verified loading, success, empty, error, and concurrent/repeated updates in the browser.

## Files Changed

Main Piece 2 changes:

```text
src/app/quote-list/quote-list-state.ts
src/app/quote-list/quote-list-state.spec.ts
src/app/quote-list/quote-list.ts
src/app/quote-list/quote-list.html
src/app/quote-list/quote-list.spec.ts
```

## Conclusion

Day 16 Piece 2 demonstrates Angular Signals, feature-specific state management, derived state, error/loading/empty handling, concurrent request protection, a practical Signals-to-Store threshold, and agent-directed development against the real Week-1 QuotesAPI.
