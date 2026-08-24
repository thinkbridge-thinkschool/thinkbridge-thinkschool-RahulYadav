# Day 13 — Piece 1: Angular 21 Signals & Zoneless

## Overview

Built an Angular 21 standalone frontend for the real Week-1 `QuotesAPI`.

The frontend uses Angular's modern signals-first approach and consumes the real API instead of mocked data.

## Real API

**Endpoint**

`GET /api/quotes/?page={page}&size={size}`

**Response fields**

- `id`
- `author`
- `text`
- `isDeleted`

The API returns a JSON array of quotes.

## Angular Implementation

- Standalone components — no NgModules
- `signal()` for reactive state
- `computed()` for derived state
- `effect()` for API refetching when pagination signals change
- `inject(HttpClient)` instead of constructor injection
- `@if` for conditional UI
- `@for` with `track quote.id` for the quote list
- Loading, empty and error states
- `provideZonelessChangeDetection()` for zoneless Angular

### Signals

Two writable signals are used:

- `page`
- `pageSize`

A computed `pageInfo` value is derived from them and displayed in the UI.

## Bug Found and Fixed

During verification, the pagination query used `Skip()` and `Take()` without an `OrderBy()`.

This could result in unpredictable page ordering and potentially overlapping or missing records.

### Fix

```csharp
.Where(q => !q.IsDeleted)
.OrderBy(q => q.Id)
.Skip((page - 1) * size)
.Take(size)
```

The API endpoint and response contract were not changed.

## Verification

- Backend build: **Passed**
- Angular `ng build`: **Passed**
- Angular `ng test`: **6/6 passed**
- No NgModules: **Verified**
- No constructor injection: **Verified**
- `@for` with `track`: **Verified**
- Real API smoke test: **Passed**
- Page 1 repeated: **Stable results**
- Page 1 vs Page 2: **Non-overlapping results**
- Loading, empty and error states: **Exercised**
- Computed value: **Verified after changing both `page` and `pageSize`**

## What Would Break if the API Contract Changes?

If the API endpoint, query parameters, or response fields such as `id`, `author`, or `text` change, the Angular service/model/template would need to be updated accordingly.

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
