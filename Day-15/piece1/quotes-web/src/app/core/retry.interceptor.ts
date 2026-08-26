import type { HttpInterceptorFn } from '@angular/common/http';
import { HttpErrorResponse } from '@angular/common/http';
import { retry, throwError, timer } from 'rxjs';

// Bounded retry-with-backoff for idempotent GET requests only.
//
// - Only GET requests are retried; POST/PUT/PATCH/DELETE always pass
//   straight through, since they are not guaranteed idempotent.
// - Only transient failures are retried: no response reached the server
//   (status 0 — network/connection failure) or a transient 5xx (500/502/503/
//   504 — Internal Server Error / Bad Gateway / Service Unavailable /
//   Gateway Timeout). A GET is idempotent, so retrying a 500 on it is safe
//   even though the server-side cause is unknown; it's still not guaranteed
//   transient, which is why POST/PUT/PATCH/DELETE never retry on it.
// - Normal 4xx validation/client errors are never retried.
// - Bounded to MAX_RETRIES attempts with exponential backoff, so a
//   persistently failing request still fails fast instead of looping.
const MAX_RETRIES = 2;
const BASE_DELAY_MS = 300;
const TRANSIENT_STATUSES = new Set([500, 502, 503, 504]);

function isTransient(error: unknown): boolean {
  return error instanceof HttpErrorResponse && (error.status === 0 || TRANSIENT_STATUSES.has(error.status));
}

export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') {
    return next(req);
  }

  return next(req).pipe(
    retry({
      count: MAX_RETRIES,
      delay: (error, retryAttempt) => {
        if (!isTransient(error)) {
          // Not transient (e.g. a 4xx) — rethrow immediately, no retry.
          return throwError(() => error);
        }
        return timer(BASE_DELAY_MS * 2 ** (retryAttempt - 1));
      },
    }),
  );
};
