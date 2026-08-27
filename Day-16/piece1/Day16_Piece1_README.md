# Day 16 — Piece 1: Routing, Lazy Loading, Guards

## Overview

This piece adds Angular routing, lazy loading, a functional authentication guard, route parameters, and View Transitions to the existing Quotes application.

The project was copied from the completed Day-15/piece1 project, so the existing HttpClient, interceptor, typed error handling, and quote functionality were preserved.

The implementation was directed through Claude Code and then reviewed and verified against the real Week-1 QuotesAPI.

## Real Week-1 API Contract

The quotes list endpoint is:

```text
GET /api/quotes?page=N&size=N
```

The quote model uses the real fields:

```text
id
author
text
```

The quote detail endpoint discovered in the actual backend is:

```text
GET /api/quotes/{id}
```

The `{id}` parameter corresponds to the real `id` field returned by the QuotesAPI.

The detail endpoint is public in the current backend.

## Routing

The application provides:

```text
/quotes
/quotes/new
/quotes/:id
```

Additional routing behavior:

```text
''          → redirect to /quotes
/login      → lazy-loaded Login
/quotes     → lazy-loaded QuotesPage
/quotes/new → lazy-loaded QuoteCreate + authGuard
/quotes/:id → lazy-loaded QuoteDetail
**          → redirect to /quotes
```

## Lazy Loading

The quote detail component is lazy-loaded instead of being included in the initial application bundle.

The production build confirmed that the quote detail component is compiled into its own approximately `3.33 kB` lazy chunk, separate from the approximately `267 kB` initial bundle.

The detail chunk is loaded when navigating to the quote detail route and was also checked using the browser DevTools Network tab.

## Functional Authentication Guard

A functional authentication guard was added using the existing authentication state.

The guard protects quote creation:

```text
Authenticated
    ↓
/quotes/new
    ↓
Allow navigation
```

For an unauthenticated user:

```text
Unauthenticated
    ↓
/quotes/new
    ↓
Redirect to /login
```

The guard uses a router `UrlTree` redirect and includes the original URL as a `returnUrl`.

The `/quotes/:id` detail route remains public because the real backend endpoint `GET /api/quotes/{id}` does not require authentication.

## Quote Detail Route

The detail route is:

```text
/quotes/:id
```

A real quote ID can be used, for example:

```text
/quotes/4
```

The detail component receives the `id` route parameter through Angular component input binding and requests the corresponding quote from:

```text
GET /api/quotes/{id}
```

The detail page displays the real quote information returned by the API.

## Invalid Route Parameters

The detail route handles invalid or non-existent IDs through the existing application error handling.

For example:

```text
/quotes/999999
```

does not result in an unhandled application crash.

## View Transitions

Angular's built-in View Transition support was enabled using:

```text
provideRouter(
  routes,
  withComponentInputBinding(),
  withViewTransitions()
)
```

This enables View Transitions for navigation between the quotes list and quote detail routes.

No additional animation library was introduced.

## Agent Review — Bug Caught

During review, an accidental backend change was found in:

```text
QuotesAPI/Models/User.cs
```

A `Name` property had been added even though it was unused, had no matching EF migration, and was unrelated to the Day 16 task.

The agent was directed to remove the accidental property and restore the Day-15 baseline.

An inaccurate authorization comment in `quote.service.ts` was also found. The comment incorrectly suggested that `POST /api/quotes` required the `can-edit-quotes` policy.

The actual backend authorization rules were checked:

```text
GET /api/quotes/{id}
→ public

POST /api/quotes
→ authenticated user required

DELETE /api/quotes/{id}
→ can-edit-quotes policy
```

The comment was corrected to match the real backend behavior.

## Verification

### Authenticated User

Verified that an authenticated user can access the quotes application and navigate to:

```text
/quotes/4
```

The detail page displayed:

- ID: `4`
- Author: `Serilog Test`
- Quote text
- Deleted status

### Unauthenticated User

After logging out, an attempt to access the protected quote creation route redirected to:

```text
/login
```

This verified the functional auth guard redirect.

### Route Parameter

Verified that:

```text
/quotes/4
```

uses the actual quote `id` value rather than a hard-coded ID.

### Lazy Loading

Verified the detail route's lazy-loaded JavaScript chunk in the browser Network tab.

The production build also confirmed a separate quote-detail lazy chunk.

### Invalid ID

Verified that a non-existent quote ID is handled through the existing error handling instead of causing an unhandled application failure.

### View Transition

Verified navigation between the quotes list and quote detail route with Angular View Transition support enabled.

## Test and Build Results

Final verification results:

```text
Angular tests:       74/74 passed
Production build:    Passed
Backend tests:        9/9 passed
```

The production build also confirmed the separate lazy-loaded quote detail chunk.

## What Breaks If the API Contract Changes?

The Angular detail implementation depends on:

```text
GET /api/quotes/{id}
```

and the quote's:

```text
id
```

field.

If the backend changes the detail endpoint, the Angular quote service must be updated.

If the backend changes the identifier from:

```text
id
```

to:

```text
quoteId
```

the quote model, route parameter handling, service request, and tests must be updated.

If the backend changes the response structure or the response returned for a missing quote, the detail component and error handling may also need to change.

The API and route tests help detect these contract changes before they silently break the frontend.

## Conclusion

Day 16 Piece 1 demonstrates:

- Angular routing
- Lazy-loaded routes
- Functional authentication guards
- Route parameters
- Quote list → quote detail navigation
- Angular View Transitions
- Verification against the real Week-1 QuotesAPI
- Agent-directed development and PR-style review

The implementation was reviewed after Claude Code generated it, an accidental backend change and incorrect authorization comment were caught and corrected, and the final project passed all Angular tests, backend tests, and the production build.
