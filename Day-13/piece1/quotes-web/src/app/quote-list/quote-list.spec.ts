import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { QuoteList } from './quote-list';

describe('QuoteList', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuoteList],
      providers: [provideHttpClient(), provideHttpClientTesting()],
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

  it('shows an error state when the API call fails', async () => {
    const fixture = TestBed.createComponent(QuoteList);
    fixture.detectChanges();
    await fixture.whenStable();

    const req = httpMock.expectOne((r) => r.url === '/api/quotes/');
    req.flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Failed to load quotes');
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

    expect(fixture.componentInstance.pageInfo()).toContain('Page 1');

    fixture.componentInstance.nextPage();
    fixture.detectChanges();
    await fixture.whenStable();

    httpMock.expectOne((r) => r.url === '/api/quotes/').flush([]);
    await fixture.whenStable();

    expect(fixture.componentInstance.pageInfo()).toContain('Page 2');
  });
});
