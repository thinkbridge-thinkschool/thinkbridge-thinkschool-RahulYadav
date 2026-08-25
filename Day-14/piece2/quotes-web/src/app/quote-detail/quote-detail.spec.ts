import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { QuoteDetail } from './quote-detail';

describe('QuoteDetail', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuoteDetail],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('shows the empty state when no quote is selected', async () => {
    const fixture = TestBed.createComponent(QuoteDetail);
    fixture.detectChanges();
    await fixture.whenStable();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Select a quote');
  });

  it('shows a loading state, then displays quote detail for the real API shape', async () => {
    const fixture = TestBed.createComponent(QuoteDetail);
    fixture.componentRef.setInput('selectedId', 1);
    fixture.detectChanges();
    await fixture.whenStable();

    // Loading state must be visible while the request is in flight.
    const loadingText = (fixture.nativeElement as HTMLElement).textContent;
    expect(loadingText).toContain('Loading quote #1');

    const req = httpMock.expectOne((r) => r.url === '/api/quotes/1');
    req.flush({ id: 1, author: 'Ada Lovelace', text: 'Test quote', isDeleted: false });
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Ada Lovelace');
    expect(compiled.textContent).toContain('Test quote');
    expect(compiled.textContent).not.toContain('Loading quote');
  });

  it('shows a not-found message on a 404', async () => {
    const fixture = TestBed.createComponent(QuoteDetail);
    fixture.componentRef.setInput('selectedId', 999);
    fixture.detectChanges();
    await fixture.whenStable();

    const req = httpMock.expectOne((r) => r.url === '/api/quotes/999');
    req.flush('Not found', { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('was not found');
  });

  it('shows a generic error message on a server error', async () => {
    const fixture = TestBed.createComponent(QuoteDetail);
    fixture.componentRef.setInput('selectedId', 2);
    fixture.detectChanges();
    await fixture.whenStable();

    const req = httpMock.expectOne((r) => r.url === '/api/quotes/2');
    req.flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Failed to load quote');
  });

  it('does not let a stale response for an earlier selection overwrite a newer one', async () => {
    const fixture = TestBed.createComponent(QuoteDetail);

    fixture.componentRef.setInput('selectedId', 1);
    fixture.detectChanges();
    await fixture.whenStable();
    const reqA = httpMock.expectOne((r) => r.url === '/api/quotes/1');

    // User quickly selects a second quote before A's response arrives.
    fixture.componentRef.setInput('selectedId', 2);
    fixture.detectChanges();
    await fixture.whenStable();

    // switchMap unsubscribes from A's inner observable as soon as B is selected,
    // so the HTTP layer itself cancels request A — it is now physically
    // impossible for A's (stale) response to reach the component.
    expect(reqA.cancelled).toBe(true);

    const reqB = httpMock.expectOne((r) => r.url === '/api/quotes/2');
    reqB.flush({ id: 2, author: 'Grace Hopper', text: 'Second quote', isDeleted: false });
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Grace Hopper');
    expect(compiled.textContent).not.toContain('Ada Lovelace');
  });
});
