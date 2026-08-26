import { Component, computed, inject, input } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { catchError, map, Observable, of, startWith, switchMap } from 'rxjs';
import { QuoteService } from '../core/quote.service';
import type { Quote } from '../core/quote.model';
import type { AppError } from '../core/app-error.model';

interface DetailState {
  loading: boolean;
  error: string | null;
  quote: Quote | null;
}

const IDLE_STATE: DetailState = { loading: false, error: null, quote: null };
const LOADING_STATE: DetailState = { loading: true, error: null, quote: null };

@Component({
  selector: 'app-quote-detail',
  imports: [],
  templateUrl: './quote-detail.html',
  styleUrl: './quote-detail.css',
})
export class QuoteDetail {
  private readonly quoteService = inject(QuoteService);

  // Selected quote id, driven by the parent. `null` means "nothing selected".
  readonly selectedId = input<number | null>(null);

  // toObservable + switchMap: switching selectedId cancels the previous in-flight
  // request, so a slow response for an earlier selection can never overwrite a
  // newer one (no stale-response race).
  private readonly state = toSignal(
    toObservable(this.selectedId).pipe(
      switchMap((id): Observable<DetailState> => {
        if (id === null) {
          return of(IDLE_STATE);
        }

        return this.quoteService.getQuoteById(id).pipe(
          map((quote): DetailState => ({ loading: false, error: null, quote })),
          startWith(LOADING_STATE),
          // errorInterceptor has already mapped this to a typed AppError;
          // the 404 case is given a more specific, id-aware message here.
          catchError((err: AppError) => {
            const message = err.status === 404 ? `Quote #${id} was not found.` : err.message;
            return of<DetailState>({ loading: false, error: message, quote: null });
          }),
        );
      }),
    ),
    { initialValue: IDLE_STATE },
  );

  readonly loading = computed(() => this.state().loading);
  readonly error = computed(() => this.state().error);
  readonly quote = computed(() => this.state().quote);
}
