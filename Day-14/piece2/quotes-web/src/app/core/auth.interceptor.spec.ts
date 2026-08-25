import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    sessionStorage.clear();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => {
    httpMock.verify();
    sessionStorage.clear();
  });

  it('does not attach an Authorization header when there is no token', () => {
    http.get('/api/quotes/').subscribe();

    const req = httpMock.expectOne('/api/quotes/');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush([]);
  });

  it('attaches the stored access token as a Bearer header on API requests', () => {
    auth.login({ email: 'ada@example.com', password: 'secret' }).subscribe();
    httpMock
      .expectOne('/api/auth/login')
      .flush({ accessToken: 'access-123', refreshToken: 'refresh-456', expiresIn: 900 });

    http.post('/api/quotes/', { author: 'Ada', text: 'Quote' }).subscribe();

    const req = httpMock.expectOne('/api/quotes/');
    expect(req.request.headers.get('Authorization')).toBe('Bearer access-123');
    req.flush({});
  });

  it('does not attach the token to requests outside the API', () => {
    auth.login({ email: 'ada@example.com', password: 'secret' }).subscribe();
    httpMock
      .expectOne('/api/auth/login')
      .flush({ accessToken: 'access-123', refreshToken: 'refresh-456', expiresIn: 900 });

    http.get('https://example.com/data').subscribe();

    const req = httpMock.expectOne('https://example.com/data');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });
});
