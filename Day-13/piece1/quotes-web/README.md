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
