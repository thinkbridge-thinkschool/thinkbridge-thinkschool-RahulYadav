import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    sessionStorage.clear();
  });

  it('starts unauthenticated with no stored token', () => {
    expect(service.isAuthenticated()).toBe(false);
    expect(service.accessToken()).toBeNull();
  });

  it('stores the access token and email on successful login', () => {
    let result: unknown;
    service.login({ email: 'ada@example.com', password: 'secret' }).subscribe((r) => (result = r));

    const req = httpMock.expectOne((r) => r.url === '/api/auth/login' && r.method === 'POST');
    expect(req.request.body).toEqual({ email: 'ada@example.com', password: 'secret' });

    req.flush({ accessToken: 'access-123', refreshToken: 'refresh-456', expiresIn: 900 });

    expect(service.isAuthenticated()).toBe(true);
    expect(service.accessToken()).toBe('access-123');
    expect(service.email()).toBe('ada@example.com');
    expect(sessionStorage.getItem('quotesApi.accessToken')).toBe('access-123');
    expect(result).toEqual({ accessToken: 'access-123', refreshToken: 'refresh-456', expiresIn: 900 });
  });

  it('never persists the password anywhere in storage', () => {
    service.login({ email: 'ada@example.com', password: 'super-secret' }).subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/auth/login');
    req.flush({ accessToken: 'access-123', refreshToken: 'refresh-456', expiresIn: 900 });

    const allValues = Object.keys(sessionStorage)
      .map((key) => sessionStorage.getItem(key))
      .join(' ');
    expect(allValues).not.toContain('super-secret');
  });

  it('remains unauthenticated after a failed login (incorrect credentials)', () => {
    let error: unknown;
    service.login({ email: 'ada@example.com', password: 'wrong' }).subscribe({
      error: (e) => (error = e),
    });

    const req = httpMock.expectOne((r) => r.url === '/api/auth/login');
    req.flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });

    expect(service.isAuthenticated()).toBe(false);
    expect(service.accessToken()).toBeNull();
    expect((error as { status: number }).status).toBe(401);
  });

  it('clears authentication state on logout after revoking the refresh token', () => {
    service.login({ email: 'ada@example.com', password: 'secret' }).subscribe();
    httpMock
      .expectOne((r) => r.url === '/api/auth/login')
      .flush({ accessToken: 'access-123', refreshToken: 'refresh-456', expiresIn: 900 });

    expect(service.isAuthenticated()).toBe(true);

    service.logout();

    const req = httpMock.expectOne((r) => r.url === '/api/auth/logout' && r.method === 'POST');
    expect(req.request.body).toEqual({ refreshToken: 'refresh-456' });
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(service.isAuthenticated()).toBe(false);
    expect(service.accessToken()).toBeNull();
    expect(service.email()).toBeNull();
    expect(sessionStorage.getItem('quotesApi.accessToken')).toBeNull();
  });

  it('clears local authentication state even if the logout request fails', () => {
    service.login({ email: 'ada@example.com', password: 'secret' }).subscribe();
    httpMock
      .expectOne((r) => r.url === '/api/auth/login')
      .flush({ accessToken: 'access-123', refreshToken: 'refresh-456', expiresIn: 900 });

    service.logout();

    httpMock
      .expectOne((r) => r.url === '/api/auth/logout')
      .flush('boom', { status: 500, statusText: 'Server Error' });

    expect(service.isAuthenticated()).toBe(false);
    expect(service.accessToken()).toBeNull();
  });
});
