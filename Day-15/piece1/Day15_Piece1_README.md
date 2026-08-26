# Day 15 — Piece 1: HttpClient + Functional Interceptors

## Overview

This piece extends the existing Day 14 Quotes application by wiring Angular `HttpClient` with functional HTTP interceptors and typed application-level error handling.

The implementation was directed through Claude Code and then reviewed and verified against the real Week-1 `QuotesAPI`.

## Real API Contract

The main endpoint used for characterization and verification is:

```text
GET /api/quotes?page=N&size=N
```

The quote data uses the real fields:

```text
id
author
text
```

The API's current validation/client-error behavior was also verified. The QuotesAPI currently returns a plain JSON string for these quote 4xx responses rather than ASP.NET Core `ProblemDetails`/`ValidationProblemDetails`.

Example:

```text
GET /api/quotes?page=0&size=5
→ 400 Bad Request
→ "Page and size must be greater than 0."
```

Another verified validation case:

```text
POST /api/quotes
(empty author)
→ 400 Bad Request
→ "Author is required."
```

The frontend handles the actual current API contract and also contains defensive support for ProblemDetails-style responses if the backend contract changes later.

## What Was Implemented

### 1. Characterization Tests

The real QuotesAPI contract was tested before the HTTP/interceptor changes.

The tests verify:

- The `/api/quotes` endpoint works.
- Pagination parameters are accepted.
- Successful quote data contains `id`, `author`, and `text`.
- A real 4xx validation response is returned by the API.
- The actual 4xx response shape is captured rather than assumed.

### 2. Angular HttpClient

The Angular application uses `HttpClient` through the existing application configuration.

The implementation uses strong TypeScript typing and does not use `any` for the quote/error models.

### 3. Functional Auth Interceptor

The authentication interceptor:

- Reads the existing authentication token.
- Adds the `Authorization` header when a token is available.
- Does not overwrite an `Authorization` header that was explicitly provided on a request.
- Does not store secrets in source control.

### 4. Retry Interceptor

The retry interceptor is intentionally restricted to idempotent GET requests.

Current behavior:

| Request | Failure | Retry |
|---|---|---|
| GET | status 0/network failure | Yes |
| GET | 500 | Yes |
| GET | 502 | Yes |
| GET | 503 | Yes |
| GET | 504 | Yes |
| GET | 4xx | No |
| POST | 5xx | No |
| PUT | 5xx | No |
| PATCH | 5xx | No |
| DELETE | 5xx | No |

The policy uses:

- Maximum 2 retries
- 3 total attempts
- Exponential backoff
- Final error propagation after retry exhaustion

### 5. Typed Error Mapping

HTTP/API failures are mapped into a typed `AppError` rather than exposing raw `HttpErrorResponse` objects directly to the UI.

The mapping supports the current API's plain-string 4xx responses and defensively supports ProblemDetails-style responses.

This allows the UI to show friendly messages instead of raw HTTP failure text.

## UI States Verified

The quotes UI was verified for:

- **Loading** — while the HTTP request is pending.
- **Success** — real quote data is displayed.
- **Empty** — empty results are handled without crashing.
- **Error** — failures surface a friendly application-level message.
- **4xx validation** — the real API 400 response is handled without retrying and surfaced through friendly error handling.

## Agent Review — Bug Caught

The first retry implementation made an incorrect assumption: it retried GET requests only for status `0` network failures and treated HTTP `500` as non-retryable.

This was caught during review because an HTTP `500` can represent a transient server failure, and GET is idempotent.

The agent was directed to:

- Retry appropriate transient 5xx GET failures, including 500.
- Keep status 0/network failures retryable.
- Never retry 4xx validation/client errors.
- Never automatically retry POST, PUT, PATCH, or DELETE.
- Keep retries bounded with backoff.

The retry tests were then updated to cover these cases explicitly.

## Verification Results

Final verification:

```text
Backend characterization tests: 9/9 passed
Angular tests:                  56/56 passed
Production build:              Passed
Live API verification:         Passed
```

The backend API contract was not changed to make the exercise pass.

## What Could Break If the API Contract Changes?

The characterization tests protect the current API contract.

Changes that could require frontend updates include:

- Changing `/api/quotes`.
- Changing the `page` or `size` query parameters.
- Renaming `id`, `author`, or `text`.
- Changing the response structure.
- Changing the 4xx response format.
- Changing backend validation rules while the frontend's validation rules remain unchanged.

For example, if the backend changes its current plain-string 400 response to `ProblemDetails`/`ValidationProblemDetails`, the error mapper must continue to correctly extract the user-facing message.

## Key Files

Important frontend additions/changes include:

```text
src/app/core/app-error.model.ts
src/app/core/app-error.mapper.ts
src/app/core/app-error.mapper.spec.ts
src/app/core/error.interceptor.ts
src/app/core/error.interceptor.spec.ts
src/app/core/retry.interceptor.ts
src/app/core/retry.interceptor.spec.ts
src/app/core/auth.interceptor.ts
src/app/core/auth.interceptor.spec.ts
```

The existing quote list, detail, and create components/tests were also updated to consume typed application errors and verify the relevant UI behavior.

Backend characterization tests were added under:

```text
QuotesAPI.Tests/
```

## Conclusion

Day 15 Piece 1 demonstrates Angular `HttpClient`, functional interceptors, authentication header handling, bounded retry-with-backoff for idempotent GETs, typed API error mapping, and UI error handling against the real Week-1 QuotesAPI.

The implementation was produced through an agent-directed workflow, reviewed for correctness, corrected where the agent made an incorrect retry assumption, and verified with the final test/build results above.
