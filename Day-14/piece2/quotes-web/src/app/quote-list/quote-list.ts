import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { QuoteService } from '../core/quote.service';
import type { Quote } from '../core/quote.model';

@Component({
  selector: 'app-quote-list',
  imports: [],
  templateUrl: './quote-list.html',
  styleUrl: './quote-list.css',
})
export class QuoteList {
  private readonly quoteService = inject(QuoteService);

  // Currently selected quote id, owned by the parent — used only to highlight a row.
  readonly selectedId = input<number | null>(null);

  // Emitted when the user picks a quote from the list.
  readonly select = output<number>();

  // Two writable signals that drive the real GET /api/quotes/?page=&size= call.
  readonly page = signal(1);
  readonly pageSize = signal(10);

  readonly quotes = signal<Quote[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  // Computed value derived from BOTH writable signals above; re-renders whenever either changes.
  readonly pageInfo = computed(() => {
    const count = this.quotes().length;
    const base = `Page ${this.page()} · ${this.pageSize()} per page`;
    if (count === 0) {
      return base;
    }
    const start = (this.page() - 1) * this.pageSize() + 1;
    const end = start + count - 1;
    return `${base} · showing ${start}-${end}`;
  });

  // QuotesApi has no totalCount field, so "more results" is inferred from a full page.
  readonly hasNextPage = computed(() => this.quotes().length === this.pageSize());

  constructor() {
    // effect(): refetches quotes whenever page or pageSize changes, keeping the
    // list in sync with the signals instead of wiring a change handler to every control.
    effect(() => {
      this.fetchQuotes(this.page(), this.pageSize());
    });
  }

  previousPage(): void {
    this.page.update((current) => Math.max(1, current - 1));
  }

  nextPage(): void {
    if (this.hasNextPage()) {
      this.page.update((current) => current + 1);
    }
  }

  changePageSize(size: number): void {
    this.pageSize.set(size);
    this.page.set(1);
  }

  selectQuote(id: number): void {
    this.select.emit(id);
  }

  private fetchQuotes(page: number, pageSize: number): void {
    this.loading.set(true);
    this.error.set(null);

    this.quoteService.getQuotes(page, pageSize).subscribe({
      next: (quotes) => {
        this.quotes.set(quotes);
        this.loading.set(false);
      },
      error: () => {
        this.quotes.set([]);
        this.error.set('Failed to load quotes from the API. Is QuotesApi running on http://localhost:5228?');
        this.loading.set(false);
      },
    });
  }
}
