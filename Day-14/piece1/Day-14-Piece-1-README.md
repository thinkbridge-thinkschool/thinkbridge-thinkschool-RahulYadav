# Day 14 --- Piece 1: Reactive Create-a-Quote Form & Authentication

## Overview

Day 14 Piece 1 extends the existing QuotesAPI Angular frontend with a
reactive **Create-a-Quote** form and a real authentication flow.

The implementation was built against the existing Week-1 QuotesAPI
contract rather than using a mock or invented API.

## Real Quote API Contract

### Create Quote

**Endpoint**

`POST /api/quotes`

**Request body**

``` json
{
  "author": "string",
  "text": "string"
}
```

The request contains only `author` and `text`. The authenticated user's
identity is derived server-side from the JWT.

### Quote Validation Constraints

The frontend mirrors the backend quote constraints:

  Field      Required   Constraint
  ---------- ---------- --------------------
  `author`   Yes        1--200 characters
  `text`     Yes        1--1000 characters

Whitespace-only values are treated as invalid because the backend uses
`IsNullOrWhiteSpace`.

## Create Quote Form

The form uses Angular Reactive Forms with strongly typed controls.

Implemented behavior:

-   Reactive form validation
-   Backend-matching field constraints
-   Required and length validation messages
-   Trimmed values before submission
-   Prevent duplicate submissions
-   Submitting state
-   API/server error handling
-   Focus moves to the first invalid field
-   Keyboard-operable controls

## Accessibility

The form includes:

-   Proper `<label>` and form-control associations
-   `aria-invalid` for invalid controls
-   `aria-describedby` connected to the corresponding error message
-   Accessible server errors using `role="alert"`
-   Keyboard navigation using standard form controls
-   First-invalid-field focus after an invalid submit
-   No reliance on color alone for error communication

## Authentication

A Login feature was added using the real QuotesAPI authentication
contract.

### Login

**Endpoint**

`POST /api/auth/login`

**Request**

``` json
{
  "email": "string",
  "password": "string"
}
```

**Response**

``` json
{
  "accessToken": "string",
  "refreshToken": "string",
  "expiresIn": 900
}
```

The frontend:

-   Uses a strongly typed login model
-   Validates required email/password fields
-   Handles incorrect credentials
-   Stores authentication state in `sessionStorage`
-   Does not store the password
-   Uses an HTTP interceptor to attach the bearer token to `/api`
    requests
-   Provides logout functionality
-   Shows the authenticated user's email
-   Shows the Create Quote form after successful authentication

## Authentication → Create Quote Flow

The application flow is:

``` text
Login
  ↓
POST /api/auth/login
  ↓
JWT access token
  ↓
HTTP interceptor
  ↓
Authorization: Bearer <token>
  ↓
POST /api/quotes
  ↓
Quote created
```

Logout clears the local authentication state and returns the application
to the Login view.

## Files Added / Updated

### Authentication

-   `src/app/core/auth.model.ts`
-   `src/app/core/auth.service.ts`
-   `src/app/core/auth.interceptor.ts`
-   `src/app/login/login.ts`
-   `src/app/login/login.html`
-   `src/app/login/login.css`

### Quote Creation

-   `src/app/core/quote.model.ts`
-   `src/app/core/quote.service.ts`
-   `src/app/quote-create/quote-create.ts`
-   `src/app/quote-create/quote-create.html`
-   `src/app/quote-create/quote-create.css`

### Application Integration

-   `src/app/app.ts`
-   `src/app/app.html`
-   `src/app/app.css`
-   `src/app/app.config.ts`

### Tests

-   `src/app/core/auth.service.spec.ts`
-   `src/app/core/auth.interceptor.spec.ts`
-   `src/app/login/login.spec.ts`
-   `src/app/quote-create/quote-create.spec.ts`
-   Updated `src/app/app.spec.ts`

## Testing

The implementation was verified with the Angular test suite.

**Result:**

-   34 tests passing
-   Build successful

Test coverage includes:

-   Empty quote form
-   Invalid author
-   Invalid text
-   Whitespace-only input
-   Successful quote creation
-   Submitting state
-   Quote server errors
-   Login validation
-   Invalid login credentials
-   Successful login
-   Authentication token persistence
-   Authorization header attachment
-   Logout
-   Accessibility attributes
-   First-invalid-field focus
-   Login → authenticated Create Quote flow

## Manual Verification

The application was also checked in the browser.

Verified states:

1.  Logged-out Login screen
2.  Invalid login/server-error state
3.  Successful authenticated state
4.  Create Quote form
5.  Empty quote validation
6.  Successful quote creation
7.  Logout

Keyboard verification included:

-   `Tab`
-   `Shift + Tab`
-   `Enter`

The form's labels, `aria-invalid`, `aria-describedby`, and first-invalid
focus behavior were checked.

A live axe/screen-reader audit was not performed; accessibility behavior
was verified through keyboard testing and automated Angular tests.

## Important Implementation Decision

One incorrect assumption was caught during implementation.

A normal Angular `Validators.required` check would not completely match
the backend because the backend treats whitespace-only values as empty
through `IsNullOrWhiteSpace`.

The implementation therefore uses a custom validator that:

1.  Trims the value.
2.  Treats an empty/whitespace-only value as required.
3.  Checks the actual backend length limits.

This keeps client-side validation aligned with the API.

## Contract Change Impact

The frontend is intentionally coupled to the real API contract.

If the backend changes:

-   **Field renamed:** update the form model, controls, template, and
    POST payload.
-   **New required field:** add the control, validator, accessibility
    wiring, and request property.
-   **Length constraint changes:** update the client validator and
    validation message.
-   **Login request/response changes:** update the authentication model
    and `AuthService`.
-   **Authentication endpoints change:** update the authentication
    service and related logout/refresh handling.

Keeping these contracts synchronized prevents client-side validation and
API behavior from drifting apart.

## Day 14 Piece 1 Status

**Completed**

The final flow is:

``` text
Authenticate
→ Create Quote
→ Validate
→ Submit
→ Receive Created Quote
→ Display Quote
→ Logout
```
