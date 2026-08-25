import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { QuoteCreate } from './quote-create';

describe('QuoteCreate', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuoteCreate],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function query<T extends Element>(fixture: { nativeElement: HTMLElement }, selector: string): T {
    const el = fixture.nativeElement.querySelector(selector);
    if (!el) {
      throw new Error(`expected to find ${selector}`);
    }
    return el as T;
  }

  it('renders labelled, accessible fields with no errors on an empty form', async () => {
    const fixture = TestBed.createComponent(QuoteCreate);
    fixture.detectChanges();
    await fixture.whenStable();

    const author = query<HTMLInputElement>(fixture, '#quote-author');
    const text = query<HTMLTextAreaElement>(fixture, '#quote-text');
    const authorLabel = query<HTMLLabelElement>(fixture, 'label[for="quote-author"]');
    const textLabel = query<HTMLLabelElement>(fixture, 'label[for="quote-text"]');

    expect(authorLabel.textContent).toContain('Author');
    expect(textLabel.textContent).toContain('Text');
    expect(author.hasAttribute('aria-invalid')).toBe(false);
    expect(text.hasAttribute('aria-invalid')).toBe(false);
    expect(fixture.nativeElement.querySelector('.field-error')).toBeNull();
  });

  it('shows a required message and aria-invalid for a blank author on submit, and moves focus to it', async () => {
    const fixture = TestBed.createComponent(QuoteCreate);
    fixture.detectChanges();
    await fixture.whenStable();

    const text = query<HTMLTextAreaElement>(fixture, '#quote-text');
    text.value = 'Some quote text';
    text.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const form = query<HTMLFormElement>(fixture, 'form');
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    await fixture.whenStable();

    const author = query<HTMLInputElement>(fixture, '#quote-author');
    const error = query<HTMLElement>(fixture, '#quote-author-error');

    expect(author.getAttribute('aria-invalid')).toBe('true');
    expect(author.getAttribute('aria-describedby')).toBe('quote-author-error');
    expect(error.textContent).toContain('Author is required.');
    expect(fixture.nativeElement.ownerDocument.activeElement).toBe(author);
  });

  it('rejects author text over the 200-character API limit', async () => {
    const fixture = TestBed.createComponent(QuoteCreate);
    fixture.detectChanges();
    await fixture.whenStable();

    const author = query<HTMLInputElement>(fixture, '#quote-author');
    author.value = 'a'.repeat(201);
    author.dispatchEvent(new Event('input'));
    author.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(author.getAttribute('aria-invalid')).toBe('true');
    const error = query<HTMLElement>(fixture, '#quote-author-error');
    expect(error.textContent).toContain('Author must be 1-200 characters.');
  });

  it('rejects whitespace-only text, matching the backend IsNullOrWhiteSpace check', async () => {
    const fixture = TestBed.createComponent(QuoteCreate);
    fixture.detectChanges();
    await fixture.whenStable();

    const text = query<HTMLTextAreaElement>(fixture, '#quote-text');
    text.value = '     ';
    text.dispatchEvent(new Event('input'));
    text.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(text.getAttribute('aria-invalid')).toBe('true');
    const error = query<HTMLElement>(fixture, '#quote-text-error');
    expect(error.textContent).toContain('Text is required.');
  });

  it('submits trimmed values, shows a submitting state, and emits the created quote', async () => {
    const fixture = TestBed.createComponent(QuoteCreate);
    fixture.detectChanges();
    await fixture.whenStable();

    const created: unknown[] = [];
    fixture.componentInstance.created.subscribe((q) => created.push(q));

    const author = query<HTMLInputElement>(fixture, '#quote-author');
    const text = query<HTMLTextAreaElement>(fixture, '#quote-text');
    author.value = '  Ada Lovelace  ';
    author.dispatchEvent(new Event('input'));
    text.value = '  A quote  ';
    text.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const button = query<HTMLButtonElement>(fixture, 'button[type="submit"]');
    const form = query<HTMLFormElement>(fixture, 'form');
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(button.disabled).toBe(true);
    expect(button.textContent).toContain('Adding');

    const req = httpMock.expectOne((r) => r.url === '/api/quotes/' && r.method === 'POST');
    expect(req.request.body).toEqual({ author: 'Ada Lovelace', text: 'A quote' });

    req.flush({ id: 42, author: 'Ada Lovelace', text: 'A quote', isDeleted: false });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(button.disabled).toBe(false);
    expect(created).toEqual([{ id: 42, author: 'Ada Lovelace', text: 'A quote', isDeleted: false }]);
  });

  it('shows an accessible error message when the API call fails', async () => {
    const fixture = TestBed.createComponent(QuoteCreate);
    fixture.detectChanges();
    await fixture.whenStable();

    const author = query<HTMLInputElement>(fixture, '#quote-author');
    const text = query<HTMLTextAreaElement>(fixture, '#quote-text');
    author.value = 'Ada Lovelace';
    author.dispatchEvent(new Event('input'));
    text.value = 'A quote';
    text.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const form = query<HTMLFormElement>(fixture, 'form');
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    const req = httpMock.expectOne((r) => r.url === '/api/quotes/' && r.method === 'POST');
    req.flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    fixture.detectChanges();

    const error = query<HTMLElement>(fixture, '#quote-create-error');
    expect(error.getAttribute('role')).toBe('alert');
    expect(error.textContent).toContain('Failed to create the quote');

    const button = query<HTMLButtonElement>(fixture, 'button[type="submit"]');
    expect(button.disabled).toBe(false);
  });
});
