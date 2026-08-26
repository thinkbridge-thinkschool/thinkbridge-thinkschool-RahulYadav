# QuotesWeb

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 21.2.21.

## Day 13 Piece 1 — verification note

**Real API contract used** (discovered in `../QuotesAPI`, not assumed):

- Endpoint: `GET /api/quotes/?page={page}&size={size}` (see `QuotesAPI/Extensions/QuoteEndpointExtensions.cs`). No authentication required for reads — `POST`/`DELETE` on the same group require a JWT, but this app only reads.
- Response shape: a bare JSON array of quotes (no pagination envelope/total count). Each item matches `QuotesApi.Models.Quote`, serialized camelCase by `System.Text.Json`:
  ```json
  { "id": 2, "author": "Policy Test", "text": "Testing authorization policy", "isDeleted": false }
  ```
- No CORS middleware is configured on the API, so the dev server proxies `/api` to `http://localhost:5228` via `proxy.conf.json` instead of calling the API cross-origin.

**Signals** (`src/app/quote-list/quote-list.ts`):
- `page = signal(1)` and `pageSize = signal(10)` — two independent writable signals.
- `quotes`, `loading`, `error` — signals holding fetch state for the loading/error/empty/list UI states.

**Computed**: `pageInfo = computed(...)` combines `page()`, `pageSize()`, and the fetched `quotes()` length into the "Page X · Y per page · showing A-B" label rendered in the toolbar. It updates whenever `page` or `pageSize` changes. `hasNextPage` is a second computed, derived from `quotes()` and `pageSize()`, used to disable the Next button (the API returns no total count, so a full page is used as the "more results" heuristic).

**Effect**: the component's `effect()` re-fetches quotes from the real API whenever `page()` or `pageSize()` changes, so pagination and page-size controls stay in sync with the network call without manual event wiring in the template.

**Why standalone**: `App` and `QuoteList` are both standalone components (`imports: [...]` on the `@Component` decorator, no `NgModule` anywhere in `src/`). `main.ts` bootstraps with `bootstrapApplication(App, appConfig)`.

**Zoneless change detection**: `src/app/app.config.ts` calls `provideZonelessChangeDetection()` (Angular 21's supported zoneless provider) in the `ApplicationConfig` providers array, alongside `provideHttpClient()` and `provideBrowserGlobalErrorListeners()`. There is no `zone.js` polyfill import.

**Dependency injection**: `HttpClient` is obtained via `inject(HttpClient)` in `QuoteService`, not constructor injection.

**Running it**:
```bash
# Terminal 1 — backend (from QuotesAPI/)
dotnet run --urls http://localhost:5228

# Terminal 2 — frontend (from quotes-web/)
npm install
ng serve
# open http://localhost:4200
```

**Verified**: `ng build` succeeds, `ng test` (Vitest) passes (6/6), and with both servers running, `GET http://localhost:4200/api/quotes/?page=1&size=3` returns real rows proxied from the live QuotesAPI/SQLite database.

**Bug found and fixed during review**: `QuoteRepository.GetQuotesAsync` (backend) used `Skip`/`Take` with no `OrderBy`, which EF Core itself flagged as producing unpredictable ordering. The Angular pagination UI (`page`/`pageSize` signals, `nextPage()`, `hasNextPage`) assumes stable, non-overlapping pages, which SQLite/EF do not guarantee without an explicit order. Fixed by adding `.OrderBy(q => q.Id)` before `Skip`/`Take` — no change to the route, query params, or response shape. Verified afterward: the EF warning no longer appears, and repeated calls to page 1 return identical results while page 1 and page 2 return disjoint id sets.

## Day 13 Piece 2 — verification note

Extends Piece 1's quote list with a quote **detail** view: selecting a quote in the list loads its detail via a second real endpoint, with explicit loading/error/empty states and switchMap-based protection against stale responses.

**Actual endpoints used** (unchanged from Piece 1, confirmed again against `QuotesAPI/Extensions/QuoteEndpointExtensions.cs`):
- `GET /api/quotes/?page={page}&size={size}` — list (bare JSON array, no pagination envelope).
- `GET /api/quotes/{id}` — detail. Returns `404` if the id doesn't exist **or** the quote is soft-deleted (`QuoteRepository.GetByIdAsync` filters `!IsDeleted`, same as the list query).

**Actual fields** (`QuotesApi.Models.Quote`, camelCase JSON): `id` (number), `author` (string), `text` (string), `isDeleted` (boolean). `core/quote.model.ts` and the new `QuoteDetail` component use exactly these — no guessed fields (no `title`, `content`, `createdAt`, etc.).

**New code**:
- `core/quote.service.ts` — added `getQuoteById(id: number)` → `GET /api/quotes/${id}`, typed `Observable<Quote>` (no `any`), `inject(HttpClient)`.
- `quote-detail/quote-detail.ts` — new standalone component. `selectedId = input<number | null>(null)`; `toObservable(selectedId).pipe(switchMap(...))` fetches the detail and is converted back with `toSignal`. `loading`, `error`, `quote` are `computed()` signals derived from that one state signal.
- `quote-list/quote-list.ts` — added `selectedId = input()` (for row highlighting) and `select = output<number>()`, emitted from a new `selectQuote(id)` on row click.
- `app.ts`/`app.html` — root now owns the `selectedId` signal and wires `QuoteList`'s `select` output into `QuoteDetail`'s `selectedId` input.

**States tested** (`quote-detail.spec.ts`, real API response shapes via `HttpTestingController`):
- detail loading — asserts "Loading quote #1…" is visible while the request is in flight, and gone after it resolves.
- detail data — flushes a real-shaped `Quote` object, asserts author/text render.
- detail error (404) — asserts a "was not found" message (matches the API's actual soft-delete/missing-id behavior).
- detail error (500) — asserts a generic "Failed to load quote" message; the error is never swallowed.
- detail empty (`selectedId === null`) — asserts the "Select a quote from the list…" prompt.
- List states (loading/error/empty/data) were already covered by Piece 1's `quote-list.spec.ts` and are untouched.

**Race condition test**: selects quote 1, then immediately selects quote 2 before the first response arrives, then flushes quote 2's response and asserts the UI shows quote 2 (never quote 1). See "genuine bug" below — this test is also what proved the fix works.

**Genuine bug/wrong assumption caught and fixed**:
- *What was wrong*: the first version of the race-condition test assumed a stale request (for the previously-selected id) would still be sitting there, flushable, after a newer selection was made — i.e. that the component merely needed to *ignore* a late response.
- *Why it was wrong*: `switchMap` unsubscribes from the previous inner observable the instant the source (`selectedId`) emits a new value. Angular's `HttpClient` observable cancels its underlying request on unsubscribe, and `provideHttpClientTesting()` mirrors that by marking the matching `TestRequest` as cancelled. Calling `.flush()` on it throws `Error: Cannot flush a cancelled request` — running `ng test` surfaced this immediately (1 failed / 10 passed).
- *The fix*: changed the test to assert `reqA.cancelled === true` right after the second selection, instead of trying to flush request A. This documents the real (and stronger) guarantee: the stale request is cancelled outright at the network layer, not merely ignored once its response lands.
- *Verification after the fix*: `ng test` → 11/11 passing (3 files: `app.spec.ts`, `quote-list.spec.ts`, `quote-detail.spec.ts`).

**Verification results**:
- `dotnet build` (QuotesAPI) — succeeds, 0 errors.
- `ng build` (quotes-web) — succeeds, 0 errors.
- `ng test` (Vitest) — 11/11 passing.
- Real API, direct (`http://localhost:5228`): `GET /api/quotes/?page=1&size=3` → real rows (ids 2, 4, 5 — 1 and 3 don't exist/are soft-deleted in the current `quotes.db`); `GET /api/quotes/2` → `200` with the real quote; `GET /api/quotes/1` and `/999999` → `404`; `GET /api/quotes/?page=0&size=10` → `400`.
- Real API, through the Angular dev-server proxy (`http://localhost:4200` → `proxy.conf.json` → `:5228`): the same list and detail calls the browser app actually issues were replayed and returned identical results, confirming the proxy path the UI depends on.
- Loading/error/empty states and the stale-response race: verified via the unit tests above (no headless-browser tool was added for this task, per "keep the implementation simple" — DOM behavior is covered by Vitest + `HttpTestingController` against the real response shapes, network wiring by the live curl checks above).

**What would break if the API contract changed**:
- Renaming/adding/removing a field on `Quote` (e.g. `author` → `authorName`) would silently produce `undefined` in the template for that field — TypeScript can't catch a shape mismatch from an untyped HTTP JSON response; `quote.model.ts` would need updating and would then be a compile error everywhere the old field was used, which is the intended safety net.
- Changing the list response from a bare array to a paginated envelope (e.g. `{ items, total }`) would break `getQuotes()`'s `Observable<Quote[]>` typing and `QuoteList`'s `quotes.set(quotes)` call.
- Changing the detail 404 behavior (e.g. returning `200` with `null`, or a different status for soft-deleted vs missing) would break the `err.status === 404` check in `quote-detail.ts` and show the generic error message instead of "was not found".
- Changing the id type from `int` to a `string`/GUID would still work end-to-end since `QuoteDetail`'s `selectedId` and `QuoteService.getQuoteById` are already typed as `number` — this would need updating in both places plus the route template.

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

To execute unit tests with the [Vitest](https://vitest.dev/) test runner, use the following command:

```bash
ng test
```

## Running end-to-end tests

For end-to-end (e2e) testing, run:

```bash
ng e2e
```

Angular CLI does not come with an end-to-end testing framework by default. You can choose one that suits your needs.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.
