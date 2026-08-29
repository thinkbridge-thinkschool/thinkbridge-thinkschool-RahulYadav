import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, withComponentInputBinding, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { routes } from '../app.routes';
import { authInterceptor } from '../core/auth.interceptor';
import { errorInterceptor } from '../core/error.interceptor';
import { retryInterceptor } from '../core/retry.interceptor';
import { AuthService } from '../core/auth.service';
import { QuotesPage } from './quotes-page';

// Integration-level tests: navigate the real app.routes config (so the
// detail route is exercised exactly as lazily-loaded in production) and
// assert on what actually rendered.
describe('QuotesPage (routing integration)', () => {
  let httpMock: HttpTestingController;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    sessionStorage.clear();

    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes, withComponentInputBinding()),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor, retryInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => {
    httpMock.verify();
    sessionStorage.clear();
  });

  it('renders the quote list and the "select a quote" placeholder at /quotes', async () => {
    await harness.navigateByUrl('/quotes', QuotesPage);
    httpMock.expectOne((r) => r.url === '/api/quotes/').flush([{ id: 1, author: 'Ada', text: 'Hi', isDeleted: false }]);
    harness.detectChanges();

    const el = harness.routeNativeElement!;
    expect(el.querySelector('app-quote-list')).toBeTruthy();
    expect(el.textContent).toContain('Select a quote from the list');
  });

  it('the empty path redirects to /quotes', async () => {
    await harness.navigateByUrl('/', QuotesPage);
    httpMock.expectOne((r) => r.url === '/api/quotes/').flush([]);

    expect(TestBed.inject(Router).url).toBe('/quotes');
  });

  it('navigating to /quotes/:id lazy-loads and renders the real quote detail using the route id', async () => {
    await harness.navigateByUrl('/quotes', QuotesPage);
    httpMock.expectOne((r) => r.url === '/api/quotes/').flush([{ id: 7, author: 'Ada', text: 'Hi', isDeleted: false }]);
    harness.detectChanges();

    await harness.navigateByUrl('/quotes/7');
    httpMock.expectOne((r) => r.url === '/api/quotes/7').flush({ id: 7, author: 'Ada Lovelace', text: 'Hi', isDeleted: false });
    harness.detectChanges();

    const el = harness.routeNativeElement!;
    expect(el.textContent).toContain('Ada Lovelace');
    expect(el.textContent).not.toContain('Select a quote from the list');
  });

  it('handles a non-numeric quote id in the URL without calling the API', async () => {
    await harness.navigateByUrl('/quotes', QuotesPage);
    httpMock.expectOne((r) => r.url === '/api/quotes/').flush([]);
    harness.detectChanges();

    await harness.navigateByUrl('/quotes/not-a-number');
    harness.detectChanges();

    expect(harness.routeNativeElement!.textContent).toContain('Invalid quote id.');
    httpMock.expectNone((r) => /\/api\/quotes\/not-a-number/.test(r.url));
  });

  it('a non-existent quote id (real API 404) shows the friendly not-found message', async () => {
    await harness.navigateByUrl('/quotes', QuotesPage);
    httpMock.expectOne((r) => r.url === '/api/quotes/').flush([]);
    harness.detectChanges();

    await harness.navigateByUrl('/quotes/999');
    httpMock.expectOne((r) => r.url === '/api/quotes/999').flush('Not found', { status: 404, statusText: 'Not Found' });
    harness.detectChanges();

    expect(harness.routeNativeElement!.textContent).toContain('Quote #999 was not found.');
  });

  it('redirects an unauthenticated user hitting /quotes/new straight to /login with a returnUrl', async () => {
    await harness.navigateByUrl('/quotes', QuotesPage);
    httpMock.expectOne((r) => r.url === '/api/quotes/').flush([]);
    harness.detectChanges();

    await harness.navigateByUrl('/quotes/new');

    expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2Fquotes%2Fnew');
  });

  it('lets an authenticated user reach /quotes/new and see the create form', async () => {
    const auth = TestBed.inject(AuthService);
    auth.login({ email: 'ada@example.com', password: 'secret' }).subscribe();
    httpMock
      .expectOne((r) => r.url === '/api/auth/login')
      .flush({ accessToken: 'access-123', refreshToken: 'refresh-456', expiresIn: 900 });

    await harness.navigateByUrl('/quotes', QuotesPage);
    httpMock.expectOne((r) => r.url === '/api/quotes/').flush([]);
    harness.detectChanges();

    await harness.navigateByUrl('/quotes/new');
    harness.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/quotes/new');
    expect(harness.routeNativeElement!.querySelector('app-quote-create')).toBeTruthy();
  });
});
