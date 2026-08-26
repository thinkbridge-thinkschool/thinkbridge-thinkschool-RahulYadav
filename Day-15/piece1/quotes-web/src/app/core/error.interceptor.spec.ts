import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { errorInterceptor } from './error.interceptor';
import type { AppError } from './app-error.model';

describe('errorInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([errorInterceptor])), provideHttpClientTesting()],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('maps a failed request into a typed AppError with a friendly message', async () => {
    const errors: AppError[] = [];
    http.get('/api/quotes/').subscribe({ error: (e: AppError) => errors.push(e) });

    // HttpTestingController.flush() takes the already-deserialized body,
    // matching how real HttpClient auto-parses a JSON string response into a
    // JS string before an interceptor ever sees it.
    httpMock.expectOne('/api/quotes/').flush('Author is required.', {
      status: 400,
      statusText: 'Bad Request',
    });
    await Promise.resolve();

    expect(errors.length).toBe(1);
    expect(errors[0].status).toBe(400);
    expect(errors[0].message).toBe('Author is required.');
    // Never the generic Angular-generated message.
    expect(errors[0].message).not.toContain('Http failure response');
  });

  it('passes a successful response through unchanged', async () => {
    const results: unknown[] = [];
    http.get('/api/quotes/').subscribe((v) => results.push(v));

    httpMock.expectOne('/api/quotes/').flush([{ id: 1, author: 'Ada', text: 'Hi', isDeleted: false }]);
    await Promise.resolve();

    expect(results).toEqual([[{ id: 1, author: 'Ada', text: 'Hi', isDeleted: false }]]);
  });
});
