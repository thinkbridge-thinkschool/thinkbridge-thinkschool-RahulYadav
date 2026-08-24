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
}
