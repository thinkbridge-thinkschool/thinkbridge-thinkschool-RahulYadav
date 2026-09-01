import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { retryInterceptor } from './retry.interceptor';

describe('retryInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([retryInterceptor])), provideHttpClientTesting()],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('GET + 500 retries with backoff and succeeds on the next attempt', async () => {
    const result: unknown[] = [];
    http.get('/api/quotes/').subscribe((v) => result.push(v));

    httpMock.expectOne('/api/quotes/').flush('boom', { status: 500, statusText: 'Server Error' });

    // Real backoff delay (300ms * 2^0) rather than faking timers.
    await new Promise((resolve) => setTimeout(resolve, 400));

    httpMock.expectOne('/api/quotes/').flush([{ id: 1 }]);
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(result).toEqual([[{ id: 1 }]]);
  });

  it('GET + status 0 (network failure) retries', async () => {
    const errors: unknown[] = [];
    http.get('/api/quotes/').subscribe({ error: (e) => errors.push(e) });

    httpMock.expectOne('/api/quotes/').error(new ProgressEvent('error'));
    await new Promise((resolve) => setTimeout(resolve, 400));

    httpMock.expectOne('/api/quotes/').flush([{ id: 1 }]);
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(errors).toEqual([]);
  });

  it('GET + 400 does not retry', async () => {
    const errors: HttpErrorResponse[] = [];
    http.get('/api/quotes/').subscribe({ error: (e) => errors.push(e) });

    httpMock.expectOne('/api/quotes/').flush('Page and size must be greater than 0.', {
      status: 400,
      statusText: 'Bad Request',
    });
    await new Promise((resolve) => setTimeout(resolve, 0));

    // httpMock.verify() in afterEach would fail if a retry request was made.
    expect(errors.length).toBe(1);
    expect(errors[0].status).toBe(400);
  });

  it('POST + 500 does not retry', async () => {
    const errors: HttpErrorResponse[] = [];
    http.post('/api/quotes/', { author: 'A', text: 'B' }).subscribe({ error: (e) => errors.push(e) });

    httpMock.expectOne('/api/quotes/').flush('boom', { status: 500, statusText: 'Server Error' });
    await new Promise((resolve) => setTimeout(resolve, 0));

    // httpMock.verify() in afterEach would fail if a retry request was made.
    expect(errors.length).toBe(1);
    expect(errors[0].status).toBe(500);
  });

  it('PUT + 503 does not retry', async () => {
    const errors: HttpErrorResponse[] = [];
    http.put('/api/quotes/1', { author: 'A', text: 'B' }).subscribe({ error: (e) => errors.push(e) });

    httpMock.expectOne('/api/quotes/1').flush('down', { status: 503, statusText: 'Service Unavailable' });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(errors.length).toBe(1);
    expect(errors[0].status).toBe(503);
  });

  it('PATCH + 503 does not retry', async () => {
    const errors: HttpErrorResponse[] = [];
    http.patch('/api/quotes/1', { author: 'A' }).subscribe({ error: (e) => errors.push(e) });

    httpMock.expectOne('/api/quotes/1').flush('down', { status: 503, statusText: 'Service Unavailable' });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(errors.length).toBe(1);
    expect(errors[0].status).toBe(503);
  });

  it('DELETE + 503 does not retry', async () => {
    const errors: HttpErrorResponse[] = [];
    http.delete('/api/quotes/1').subscribe({ error: (e) => errors.push(e) });

    httpMock.expectOne('/api/quotes/1').flush('down', { status: 503, statusText: 'Service Unavailable' });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(errors.length).toBe(1);
    expect(errors[0].status).toBe(503);
  });

  it('retry exhaustion (bounded to 2 retries / 3 attempts) preserves the final error', async () => {
    const errors: HttpErrorResponse[] = [];
    http.get('/api/quotes/').subscribe({ error: (e) => errors.push(e) });

    // Attempt 1
    httpMock.expectOne('/api/quotes/').flush('down', { status: 500, statusText: 'Server Error' });
    await new Promise((resolve) => setTimeout(resolve, 400));

    // Attempt 2 (retry 1)
    httpMock.expectOne('/api/quotes/').flush('down', { status: 500, statusText: 'Server Error' });
    await new Promise((resolve) => setTimeout(resolve, 800));

    // Attempt 3 (retry 2, final — MAX_RETRIES exhausted)
    httpMock.expectOne('/api/quotes/').flush('down', { status: 500, statusText: 'Server Error' });
    await new Promise((resolve) => setTimeout(resolve, 0));

    // No further (4th) request is made — the final attempt's error propagates
    // unmodified instead of retrying forever.
    expect(errors.length).toBe(1);
    expect(errors[0].status).toBe(500);
  });
});
