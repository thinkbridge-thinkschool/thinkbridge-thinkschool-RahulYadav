import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, withComponentInputBinding, Router } from '@angular/router';

import { App } from './app';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth.interceptor';
import { errorInterceptor } from './core/error.interceptor';
import { retryInterceptor } from './core/retry.interceptor';
import { AuthService } from './core/auth.service';

describe('App', () => {
  let httpMock: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    sessionStorage.clear();

    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter(routes, withComponentInputBinding()),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor, retryInterceptor])),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    httpMock.verify();
    sessionStorage.clear();
  });

  it('should create the app and render its title', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await router.navigateByUrl('/quotes');
    fixture.detectChanges();
    httpMock.expectOne((req) => req.url === '/api/quotes/').flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.componentInstance).toBeTruthy();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('QuotesApi frontend');
  });

  it('routes / to the quote list via /quotes', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await router.navigateByUrl('/');
    fixture.detectChanges();
    httpMock.expectOne((req) => req.url === '/api/quotes/').flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(router.url).toBe('/quotes');
    expect((fixture.nativeElement as HTMLElement).querySelector('app-quote-list')).toBeTruthy();
  });

  it('shows a "Log in" link (not the auth status) when logged out', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.login-link')).toBeTruthy();
    expect(compiled.querySelector('.auth-status')).toBeNull();
  });

  it('shows the signed-in status and hides the login link once authenticated', async () => {
    const auth = TestBed.inject(AuthService);
    auth.login({ email: 'ada@example.com', password: 'secret' }).subscribe();
    httpMock
      .expectOne((r) => r.url === '/api/auth/login')
      .flush({ accessToken: 'access-123', refreshToken: 'refresh-456', expiresIn: 900 });

    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.auth-status')?.textContent).toContain('ada@example.com');
    expect(compiled.querySelector('.login-link')).toBeNull();
  });
});
