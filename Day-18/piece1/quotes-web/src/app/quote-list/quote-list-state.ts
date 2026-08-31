import { Injectable, computed, inject, signal } from '@angular/core';
import { QuoteService } from '../core/quote.service';
import type { Quote } from '../core/quote.model';
import type { AppError } from '../core/app-error.model';

// Feature-specific signal state for the quotes list (GET /api/quotes/?page=&size=).
// Owns loading/data/error/pagination state so QuoteList only has to render it.
// Provided per-QuoteList-instance (see quote-list.ts `providers`), not root,
// so state resets whenever the component (re)mounts instead of leaking
// across navigations away from and back to /quotes.
@Injectable()
export class QuoteListState {
  private readonly quoteService = inject(QuoteService);

  private readonly _quotes = signal<Quote[]>([]);
  private readonly _loading = signal(false);
  private readonly _error = signal<AppError | null>(null);

  readonly quotes = this._quotes.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();

  // Friendly message for display, derived from the typed error — nothing
  // duplicated, just a projection of `error`.
  readonly errorMessage = computed(() => this._error()?.message ?? null);

  // Derived, not a separately-set boolean: empty only means "a request
  // succeeded and came back with nothing", not "loading" or "failed".
  readonly isEmpty = computed(
    () => !this._loading() && !this._error() && this._quotes().length === 0,
  );

  readonly page = signal(1);
  readonly pageSize = signal(10);

  // QuotesApi has no totalCount field, so "more results" is inferred from a full page.
  readonly hasNextPage = computed(() => this._quotes().length === this.pageSize());

  readonly pageInfo = computed(() => {
    const count = this._quotes().length;
    const base = `Page ${this.page()} · ${this.pageSize()} per page`;
    if (count === 0) {
      return base;
    }
    const start = (this.page() - 1) * this.pageSize() + 1;
    const end = start + count - 1;
    return `${base} · showing ${start}-${end}`;
  });

  // Bumped on every loadQuotes() call and captured per in-flight request.
  // If loadQuotes() is called again before an earlier call's response
  // arrives (e.g. rapid pagination clicks), the earlier response is
  // recognized as stale by its captured id no longer matching the latest
  // one, and is dropped instead of clobbering newer state. This keeps the
  // service a plain signal + subscribe (no RxJS pipeline) while still
  // guaranteeing the visible state always reflects the most recently
  // *requested* page, never an out-of-order response.
  private latestRequestId = 0;

  loadQuotes(page: number, pageSize: number): void {
    this.page.set(page);
    this.pageSize.set(pageSize);

    const requestId = ++this.latestRequestId;
    this._loading.set(true);
    this._error.set(null);

    this.quoteService.getQuotes(page, pageSize).subscribe({
      next: (quotes) => {
        if (requestId !== this.latestRequestId) {
          return; // A newer loadQuotes() call has superseded this response.
        }
        this._quotes.set(quotes);
        this._loading.set(false);
      },
      // errorInterceptor has already mapped this to a typed AppError with a
      // friendly message — no raw HttpErrorResponse reaches this service.
      error: (err: AppError) => {
        if (requestId !== this.latestRequestId) {
          return;
        }
        this._quotes.set([]);
        this._error.set(err);
        this._loading.set(false);
      },
    });
  }

  previousPage(): void {
    this.loadQuotes(Math.max(1, this.page() - 1), this.pageSize());
  }

  nextPage(): void {
    if (this.hasNextPage()) {
      this.loadQuotes(this.page() + 1, this.pageSize());
    }
  }

  changePageSize(size: number): void {
    this.loadQuotes(1, size);
  }
}
