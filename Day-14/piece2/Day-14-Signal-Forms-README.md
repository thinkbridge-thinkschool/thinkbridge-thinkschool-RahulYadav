# Day 14 --- Signal Forms Preview

## Overview

This piece rebuilds the existing Create-a-Quote form using Angular's
Signal Forms preview API while keeping the real Week-1 QuotesAPI
contract unchanged.

## Real Quote API Contract

**Endpoint:** `POST /api/quotes`

**Request body:**

``` json
{
  "author": "string",
  "text": "string"
}
```

The request contains exactly `author` and `text`. No `userId` is sent.

## Validation Constraints

  Field      Required   Constraint
  ---------- ---------- --------------------
  `author`   Yes        1--200 characters
  `text`     Yes        1--1000 characters

Whitespace-only values are invalid because the backend uses
`IsNullOrWhiteSpace`.

## Signal Forms Implementation

The Create Quote form uses Angular Signal Forms APIs including:

-   `form()`
-   `validate()`
-   `[formRoot]`
-   `[formField]`
-   Signal Forms field state
-   Signal Forms submission handling

The existing `QuoteService.createQuote()` sends the request to
`POST /api/quotes`.

## States Verified

### Pristine

The form opens without validation errors or `aria-invalid`.

### Dirty

Changing a field makes the corresponding Signal Forms field dirty.

### Touched

Focusing and leaving an invalid field marks it touched and displays its
validation error.

### Validators

Tested:

-   Empty author
-   Whitespace-only author
-   Author over 200 characters
-   Empty text
-   Whitespace-only text
-   Text over 1000 characters

### Clean Submit

``` text
Author: Albert Einstein
Text: Life is beautiful.
```

The exact request sent is:

``` json
{
  "author": "Albert Einstein",
  "text": "Life is beautiful."
}
```

The button enters the submitting state and the form resets after
success.

### Failed Submit

A failed API response was exercised. The form displays an accessible
server error, exits the submitting state, and remains usable.

## Accessibility

Verified:

-   Associated labels
-   Keyboard navigation
-   `Tab`
-   `Shift + Tab`
-   `Enter`
-   `aria-invalid`
-   `aria-describedby`
-   Matching error element IDs
-   First-invalid-field focus
-   Accessible server-error handling

A live axe/screen-reader audit was not possible because of environment
disk-space limitations. Accessibility was checked through keyboard
testing and automated DOM assertions.

## Concrete Bug / Wrong Assumption Found

The Signal Forms submission action uses `firstValueFrom()` to bridge the
RxJS HTTP request into the async submission API. The resulting
Promise/microtask was not automatically tracked by Angular's zoneless
`fixture.whenStable()`.

Two tests initially failed because assertions ran before the submission
Promise completed.

The agent fixed this by adding an explicit microtask-flush helper before
`whenStable()`.

The agent also fixed a second issue where `reset()` and `created.emit()`
were inside the HTTP `try` block, narrowing the `try` so UI exceptions
could not incorrectly become server errors.

## Signal Forms vs Reactive Forms

**Simpler:** Signal Forms reduces some boilerplate around form state,
submission state, and focus handling.

**Rougher:** It is still experimental. Custom validation is needed for
whitespace-specific backend behavior, accessibility wiring remains
largely manual, and async testing can require additional microtask
handling.

**Conclusion:** For production today, I would still choose Reactive
Forms for this form because it is more mature. Signal Forms is promising
but should not yet be considered full parity with Reactive Forms.

## Tests

The implementation was type-checked and the complete test suite passed.

**Result: 34 tests passing.**

Tests cover validation, accessibility attributes, focus behavior, clean
submission, exact POST body, failed submission, and async submission
behavior.

## Files Changed

-   `quotes-web/src/app/quote-create/quote-create.ts`
-   `quotes-web/src/app/quote-create/quote-create.html`
-   `quotes-web/src/app/quote-create/quote-create.spec.ts`

Login, authentication, QuoteList, QuoteDetail, core API infrastructure,
and existing styling were left untouched.

## Contract Change Impact

If the API contract changes:

-   Renaming `author` or `text` requires updating the model, Signal
    Forms field, template, validation, and POST payload.
-   Adding a required field requires a new form field, validator, label,
    error message, accessibility wiring, and request property.
-   Tightening length limits requires updating the Signal Forms
    validators and messages.
-   Changing the endpoint requires updating
    `QuoteService.createQuote()`.

The TypeScript model provides compile-time protection against
request-shape mismatches.

## Status

**Day 14 --- Signal Forms: Completed**

``` text
Login
  ↓
Authenticated Create Quote
  ↓
Signal Forms validation
  ↓
POST /api/quotes
  ↓
Quote created
  ↓
Form reset
```
