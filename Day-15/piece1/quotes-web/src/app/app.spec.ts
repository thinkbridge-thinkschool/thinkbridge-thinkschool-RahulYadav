import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { App } from './app';
import { authInterceptor } from './core/auth.interceptor';

describe('App', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    sessionStorage.clear();

    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    sessionStorage.clear();
  });

  it('should create the app', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.componentInstance).toBeTruthy();

    httpMock.expectOne((req) => req.url === '/api/quotes/').flush([]);
  });

  it('should render the title and the quote list', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();

    httpMock.expectOne((req) => req.url === '/api/quotes/').flush([]);
    await fixture.whenStable();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('QuotesApi frontend');
    expect(compiled.querySelector('app-quote-list')).toBeTruthy();
  });

  it('shows the login form (not the create form) when logged out', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    httpMock.expectOne((req) => req.url === '/api/quotes/').flush([]);

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('app-login')).toBeTruthy();
    expect(compiled.querySelector('app-quote-create')).toBeNull();
  });

  // The quote list/detail panes keep running in the background throughout this
  // test (independent of login state), so drain whatever GET requests they've
  // issued rather than assuming an exact count.
  function drainBackgroundGetRequests(): void {
    httpMock.match((req) => req.method === 'GET').forEach((req) => {
      if (/\/api\/quotes\/\d+$/.test(req.request.url)) {
        req.flush({ id: 1, author: 'Ada Lovelace', text: 'A quote', isDeleted: false });
      } else {
        req.flush([]);
      }
    });
  }

  it('shows authenticated state and the create form after logging in, and attaches the token to POST /api/quotes', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    drainBackgroundGetRequests();

    const compiled = fixture.nativeElement as HTMLElement;
    const emailInput = compiled.querySelector<HTMLInputElement>('#login-email')!;
    const passwordInput = compiled.querySelector<HTMLInputElement>('#login-password')!;
    emailInput.value = 'ada@example.com';
    emailInput.dispatchEvent(new Event('input'));
    passwordInput.value = 'secret';
    passwordInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    compiled.querySelector('app-login form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    httpMock
      .expectOne((req) => req.url === '/api/auth/login')
      .flush({ accessToken: 'access-123', refreshToken: 'refresh-456', expiresIn: 900 });
    await fixture.whenStable();
    fixture.detectChanges();
    drainBackgroundGetRequests();

    expect(compiled.querySelector('app-login')).toBeNull();
    expect(compiled.querySelector('app-quote-create')).toBeTruthy();
    expect(compiled.querySelector('.auth-status')?.textContent).toContain('ada@example.com');

    const author = compiled.querySelector<HTMLInputElement>('#quote-author')!;
    const text = compiled.querySelector<HTMLTextAreaElement>('#quote-text')!;
    author.value = 'Ada Lovelace';
    author.dispatchEvent(new Event('input'));
    text.value = 'A quote';
    text.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    compiled.querySelector('app-quote-create form')!.dispatchEvent(new Event('submit'));

    const createReq = httpMock.expectOne((req) => req.url === '/api/quotes/' && req.method === 'POST');
    expect(createReq.request.headers.get('Authorization')).toBe('Bearer access-123');
    createReq.flush({ id: 1, author: 'Ada Lovelace', text: 'A quote', isDeleted: false });
    await fixture.whenStable();
    fixture.detectChanges();
    drainBackgroundGetRequests();

    // Logging out returns to the login form and clears the auth status.
    const logoutButton = Array.from(compiled.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Log out'),
    )!;
    logoutButton.click();

    httpMock.expectOne((req) => req.url === '/api/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();
    fixture.detectChanges();
    drainBackgroundGetRequests();

    expect(compiled.querySelector('app-login')).toBeTruthy();
    expect(compiled.querySelector('app-quote-create')).toBeNull();
    expect(compiled.querySelector('.auth-status')).toBeNull();
  });
});
