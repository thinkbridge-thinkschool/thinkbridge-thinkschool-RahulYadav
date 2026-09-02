import { HttpErrorResponse } from '@angular/common/http';
import type { AppError, ValidationErrors } from './app-error.model';

interface ProblemDetailsBody {
  title?: string;
  detail?: string;
  errors?: ValidationErrors;
}

function isProblemDetailsBody(body: unknown): body is ProblemDetailsBody {
  return (
    typeof body === 'object' &&
    body !== null &&
    ('title' in body || 'detail' in body || 'errors' in body)
  );
}

// Maps a failed HTTP response into a typed AppError with a friendly message.
//
// IMPORTANT: the real QuotesApi (see QuoteEndpointExtensions.cs) does not use
// ASP.NET Core ProblemDetails/ValidationProblemDetails. Every 4xx from the
// quotes endpoints is `Results.BadRequest(<plain string>)`, which serializes
// as a bare JSON string body — confirmed by QuotesApi.Tests and by exercising
// the running API directly. That real shape is handled first below. The
// ProblemDetails branch is defensive: it only activates if a response body
// already looks like ProblemDetails, so nothing here fabricates fields the
// API doesn't send today, but the app won't break if the backend later adds
// `AddProblemDetails()`.
export function toAppError(error: HttpErrorResponse): AppError {
  if (error.status === 0) {
    return {
      status: 0,
      message: 'Could not reach the QuotesApi server. Check your connection and try again.',
    };
  }

  if (error.status === 401) {
    return { status: 401, message: 'You need to sign in to do that.' };
  }

  if (error.status === 403) {
    return { status: 403, message: 'You do not have permission to do that.' };
  }

  if (error.status === 404) {
    return { status: 404, message: 'The requested resource could not be found.' };
  }

  if (error.status >= 500) {
    return {
      status: error.status,
      message: 'Something went wrong on the server. Please try again later.',
    };
  }

  // Remaining 4xx: the real, current QuotesApi validation contract is a bare
  // JSON string body (e.g. "Author is required.").
  if (typeof error.error === 'string' && error.error.trim().length > 0) {
    return { status: error.status, message: error.error, detail: error.error };
  }

  if (isProblemDetailsBody(error.error)) {
    const problem = error.error;
    return {
      status: error.status,
      message: problem.detail ?? problem.title ?? 'The request was invalid.',
      detail: problem.detail,
      validationErrors: problem.errors,
    };
  }

  return {
    status: error.status,
    message: 'The request was invalid. Please check your input and try again.',
  };
}
