import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Quote } from './quote.model';

@Injectable({ providedIn: 'root' })
export class QuoteService {
  private readonly http = inject(HttpClient);

  // GET /api/quotes/?page={page}&size={size} — real QuotesApi endpoint.
  // Returns a bare JSON array, no pagination metadata.
  getQuotes(page: number, size: number) {
    return this.http.get<Quote[]>('/api/quotes/', {
      params: { page, size },
    });
  }

  // GET /api/quotes/{id} — real QuotesApi endpoint.
  // Returns 404 if the id doesn't exist or the quote is soft-deleted.
  getQuoteById(id: number) {
    return this.http.get<Quote>(`/api/quotes/${id}`);
  }
}
