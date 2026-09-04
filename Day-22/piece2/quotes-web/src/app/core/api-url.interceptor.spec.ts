import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { apiUrlInterceptor } from './api-url.interceptor';
import { environment } from '../../environments/environment';

describe('apiUrlInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiUrlInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    environment.apiBaseUrl = '';
  });

  it('leaves the request untouched when apiBaseUrl is empty (dev)', () => {
    http.get('/api/quotes/').subscribe();

    const req = httpMock.expectOne('/api/quotes/');
    req.flush([]);
  });

  it('rewrites /api requests to the absolute origin when apiBaseUrl is set (prod)', () => {
    environment.apiBaseUrl = 'https://quotes-api-final.example.azurecontainerapps.io';

    http.get('/api/quotes/').subscribe();

    const req = httpMock.expectOne('https://quotes-api-final.example.azurecontainerapps.io/api/quotes/');
    req.flush([]);
  });

  it('does not rewrite requests outside /api', () => {
    environment.apiBaseUrl = 'https://quotes-api-final.example.azurecontainerapps.io';

    http.get('/assets/config.json').subscribe();

    const req = httpMock.expectOne('/assets/config.json');
    req.flush({});
  });
});
