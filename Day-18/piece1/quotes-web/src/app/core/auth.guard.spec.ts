import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';

@Component({ template: 'protected' })
class ProtectedStub {}

@Component({ template: 'login' })
class LoginStub {}

describe('authGuard', () => {
  beforeEach(() => {
    sessionStorage.clear();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'protected', component: ProtectedStub, canActivate: [authGuard] },
          { path: 'login', component: LoginStub },
        ]),
      ],
    });
  });

  afterEach(() => sessionStorage.clear());

  it('allows navigation to the protected route for an authenticated user', async () => {
    const auth = TestBed.inject(AuthService);
    const httpMock = TestBed.inject(HttpTestingController);

    auth.login({ email: 'ada@example.com', password: 'secret' }).subscribe();
    httpMock
      .expectOne((r) => r.url === '/api/auth/login')
      .flush({ accessToken: 'access-123', refreshToken: 'refresh-456', expiresIn: 900 });
    expect(auth.isAuthenticated()).toBe(true);

    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl('/protected', ProtectedStub);

    expect(component).toBeInstanceOf(ProtectedStub);
    expect(TestBed.inject(Router).url).toBe('/protected');
  });

  it('redirects an unauthenticated user to /login and never activates the protected route', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/protected');

    expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2Fprotected');
  });

  it('returns a UrlTree redirect rather than navigating imperatively', () => {
    TestBed.runInInjectionContext(() => {
      const result = authGuard(
        { } as never,
        { url: '/protected' } as never,
      );

      expect(result).toBeInstanceOf(UrlTree);
    });
  });
});
