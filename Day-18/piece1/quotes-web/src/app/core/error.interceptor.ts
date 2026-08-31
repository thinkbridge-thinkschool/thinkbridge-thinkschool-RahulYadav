import type { HttpInterceptorFn } from '@angular/common/http';
import { HttpErrorResponse } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { toAppError } from './app-error.mapper';

// Maps every failed API response into a typed AppError so components never
// have to interpret a raw HttpErrorResponse. Placed after retryInterceptor
// in app.config.ts so it only maps the final, retry-exhausted error.
export const errorInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse) {
        return throwError(() => toAppError(error));
      }
      throw error;
    }),
  );
