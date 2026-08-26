import { HttpErrorResponse } from '@angular/common/http';
import { toAppError } from './app-error.mapper';

describe('toAppError', () => {
  it('maps the real QuotesApi 400 shape: a bare JSON string body', () => {
    // QuoteEndpointExtensions.cs -> Results.BadRequest(string); this is what
    // the running API actually returns, confirmed via QuotesApi.Tests and a
    // live curl against GET /api/quotes?page=0.
    const error = new HttpErrorResponse({
      status: 400,
      statusText: 'Bad Request',
      error: 'Page and size must be greater than 0.',
    });

    const appError = toAppError(error);

    expect(appError.status).toBe(400);
    expect(appError.message).toBe('Page and size must be greater than 0.');
    expect(appError.validationErrors).toBeUndefined();
  });

  it('maps a genuine ASP.NET Core ValidationProblemDetails body defensively', () => {
    // Not returned by this API today, but this is the documented .NET shape
    // — handled so the app keeps working if the backend later adopts
    // AddProblemDetails()/ValidationProblem().
    const error = new HttpErrorResponse({
      status: 400,
      statusText: 'Bad Request',
      error: {
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { Author: ['Author is required.'] },
      },
    });

    const appError = toAppError(error);

    expect(appError.status).toBe(400);
    expect(appError.message).toBe('One or more validation errors occurred.');
    expect(appError.validationErrors).toEqual({ Author: ['Author is required.'] });
  });

  it('maps 401 to a friendly sign-in message', () => {
    const appError = toAppError(new HttpErrorResponse({ status: 401 }));
    expect(appError.message).toBe('You need to sign in to do that.');
  });

  it('maps 403 to a friendly permission message', () => {
    const appError = toAppError(new HttpErrorResponse({ status: 403 }));
    expect(appError.message).toBe('You do not have permission to do that.');
  });

  it('maps 404 to a friendly not-found message', () => {
    const appError = toAppError(new HttpErrorResponse({ status: 404 }));
    expect(appError.message).toBe('The requested resource could not be found.');
  });

  it('maps a 5xx to a generic server-error message, never the raw body', () => {
    const appError = toAppError(
      new HttpErrorResponse({ status: 500, error: '<html>Internal Server Error</html>' }),
    );
    expect(appError.message).toBe('Something went wrong on the server. Please try again later.');
    expect(appError.message).not.toContain('<html>');
  });

  it('maps status 0 (no response reached the server) to a network message', () => {
    const appError = toAppError(new HttpErrorResponse({ status: 0 }));
    expect(appError.status).toBe(0);
    expect(appError.message).toContain('Could not reach the QuotesApi server');
  });
});
