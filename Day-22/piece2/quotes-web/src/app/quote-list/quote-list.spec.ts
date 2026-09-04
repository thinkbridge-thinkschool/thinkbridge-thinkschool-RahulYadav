import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { QuoteList } from './quote-list';
import { errorInterceptor } from '../core/error.interceptor';
import { retryInterceptor } from '../core/retry.interceptor';

describe('QuoteList', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuoteList],
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor, retryInterceptor])),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should create', async () => {
    const fixture = TestBed.createComponent(QuoteList);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.componentInstance).toBeTruthy();

    httpMock.expectOne((req) => req.url === '/api/quotes/').flush([]);
  });

  it('renders quotes returned by the real API shape', async () => {
    const fixture = TestBed.createComponent(QuoteList);
    fixture.detectChanges();
    await fixture.whenStable();

    const req = httpMock.expectOne((r) => r.url === '/api/quotes/');
    req.flush([{ id: 1, author: 'Ada Lovelace', text: 'Test quote', isDeleted: false }]);
    await fixture.whenStable();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Ada Lovelace');
    expect(compiled.textContent).toContain('Test quote');
  });

  it('shows a friendly mapped error state when the API call fails and retries are exhausted (transient 500)', async () => {
    const fixture = TestBed.createComponent(QuoteList);
    fixture.detectChanges();
    await fixture.whenStable();

    // A 500 is now treated as a transient GET failure, so retryInterceptor
    // retries it (bounded to 2 retries / 3 attempts total) before giving up.
    httpMock.expectOne((r) => r.url === '/api/quotes/').flush('boom', { status: 500, statusText: 'Server Error' });
    await new Promise((resolve) => setTimeout(resolve, 400));

    httpMock.expectOne((r) => r.url === '/api/quotes/').flush('boom', { status: 500, statusText: 'Server Error' });
    await new Promise((resolve) => setTimeout(resolve, 800));

    httpMock.expectOne((r) => r.url === '/api/quotes/').flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    // No further (4th) request is made once retries are exhausted, and
    // errorInterceptor maps the final error to a friendly AppError message
    // instead of the raw "Http failure response..." text.
    httpMock.verify();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Something went wrong on the server');
    expect(compiled.textContent).not.toContain('Http failure response');
  });

  it('retries a transient 503 GET failure with backoff before succeeding', async () => {
    const fixture = TestBed.createComponent(QuoteList);
    fixture.detectChanges();
    await fixture.whenStable();

    httpMock.expectOne((r) => r.url === '/api/quotes/').flush('unavailable', { status: 503, statusText: 'Service Unavailable' });

    // Real backoff delay (300ms * 2^0) rather than faking timers, to avoid
    // interfering with zoneless change detection's own scheduling.
    await new Promise((resolve) => setTimeout(resolve, 400));

    httpMock
      .expectOne((r) => r.url === '/api/quotes/')
      .flush([{ id: 1, author: 'Ada Lovelace', text: 'Retried quote', isDeleted: false }]);
    await fixture.whenStable();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Retried quote');
  });

  it('does not retry a 400 validation error', async () => {
    const fixture = TestBed.createComponent(QuoteList);
    fixture.detectChanges();
    await fixture.whenStable();

    httpMock.expectOne((r) => r.url === '/api/quotes/').flush('Page and size must be greater than 0.', {
      status: 400,
      statusText: 'Bad Request',
    });
    await fixture.whenStable();

    // No second request should have been made for a non-transient 4xx.
    httpMock.verify();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Page and size must be greater than 0.');
  });

  it('updates pageInfo when the page or pageSize signal changes', async () => {
    const fixture = TestBed.createComponent(QuoteList);
    fixture.detectChanges();
    await fixture.whenStable();

    const fullPage = Array.from({ length: 10 }, (_, i) => ({
      id: i + 1,
      author: `Author ${i + 1}`,
      text: `Quote ${i + 1}`,
      isDeleted: false,
    }));
    httpMock.expectOne((r) => r.url === '/api/quotes/').flush(fullPage);
    await fixture.whenStable();

    expect(fixture.componentInstance.state.pageInfo()).toContain('Page 1');

    fixture.componentInstance.nextPage();
    fixture.detectChanges();
    await fixture.whenStable();

    httpMock.expectOne((r) => r.url === '/api/quotes/').flush([]);
    await fixture.whenStable();

    expect(fixture.componentInstance.state.pageInfo()).toContain('Page 2');
  });
});
