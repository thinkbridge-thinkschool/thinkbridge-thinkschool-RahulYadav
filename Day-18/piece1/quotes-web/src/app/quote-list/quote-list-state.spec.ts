import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { QuoteListState } from './quote-list-state';
import { errorInterceptor } from '../core/error.interceptor';

describe('QuoteListState', () => {
  let state: QuoteListState;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        QuoteListState,
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    state = TestBed.inject(QuoteListState);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('starts with empty quotes, not loading, no error', () => {
    expect(state.quotes()).toEqual([]);
    expect(state.loading()).toBe(false);
    expect(state.error()).toBeNull();
    // isEmpty is derived purely from {loading, error, quotes}; before the
    // first loadQuotes() call that combination is indistinguishable from a
    // real empty result. QuoteList always calls loadQuotes() on construction
    // (see quote-list.ts), so this transient value is never actually
    // rendered — loading flips true synchronously before change detection runs.
    expect(state.isEmpty()).toBe(true);
  });

  it('sets loading while the request is in flight', () => {
    state.loadQuotes(1, 10);

    expect(state.loading()).toBe(true);
    expect(state.error()).toBeNull();

    httpMock.expectOne((r) => r.url === '/api/quotes/').flush([]);
  });

  it('stores quotes from a successful response using the real quote shape', () => {
    state.loadQuotes(1, 10);

    httpMock
      .expectOne((r) => r.url === '/api/quotes/')
      .flush([{ id: 1, author: 'Ada Lovelace', text: 'Test quote', isDeleted: false }]);

    expect(state.loading()).toBe(false);
    expect(state.error()).toBeNull();
    expect(state.quotes()).toEqual([{ id: 1, author: 'Ada Lovelace', text: 'Test quote', isDeleted: false }]);
    expect(state.isEmpty()).toBe(false);
  });

  it('derives isEmpty from an empty successful response instead of a separate flag', () => {
    state.loadQuotes(1, 10);

    httpMock.expectOne((r) => r.url === '/api/quotes/').flush([]);

    expect(state.loading()).toBe(false);
    expect(state.error()).toBeNull();
    expect(state.quotes()).toEqual([]);
    expect(state.isEmpty()).toBe(true);
  });

  it('stores the typed AppError and a friendly message on failure, and resets loading', () => {
    state.loadQuotes(1, 10);

    httpMock
      .expectOne((r) => r.url === '/api/quotes/')
      .flush('boom', { status: 500, statusText: 'Server Error' });

    expect(state.loading()).toBe(false);
    expect(state.error()?.status).toBe(500);
    expect(state.errorMessage()).toContain('Something went wrong on the server');
    expect(state.quotes()).toEqual([]);
    expect(state.isEmpty()).toBe(false); // failed, not "loaded and empty"
  });

  it('does not leave loading stuck true after an error', () => {
    state.loadQuotes(1, 10);
    expect(state.loading()).toBe(true);

    httpMock.expectOne((r) => r.url === '/api/quotes/').flush('nope', { status: 404, statusText: 'Not Found' });

    expect(state.loading()).toBe(false);
  });

  it('ignores a stale response when loadQuotes() is called again before the first resolves', () => {
    // Simulates rapid pagination: page 1 requested, then page 2 before page 1's
    // response arrives. Page 1's (older) response must not clobber page 2's data.
    state.loadQuotes(1, 10);
    const firstReq = httpMock.expectOne((r) => r.url === '/api/quotes/' && r.params.get('page') === '1');

    state.loadQuotes(2, 10);
    const secondReq = httpMock.expectOne((r) => r.url === '/api/quotes/' && r.params.get('page') === '2');

    // Second (newer) request resolves first.
    secondReq.flush([{ id: 2, author: 'Grace Hopper', text: 'Second', isDeleted: false }]);
    expect(state.quotes()).toEqual([{ id: 2, author: 'Grace Hopper', text: 'Second', isDeleted: false }]);
    expect(state.loading()).toBe(false);

    // First (stale) request resolves after — must be dropped, not applied.
    firstReq.flush([{ id: 1, author: 'Ada Lovelace', text: 'First', isDeleted: false }]);
    expect(state.quotes()).toEqual([{ id: 2, author: 'Grace Hopper', text: 'Second', isDeleted: false }]);
    expect(state.loading()).toBe(false);
    expect(state.page()).toBe(2);
  });

  it('ignores a stale error from a superseded request', () => {
    state.loadQuotes(1, 10);
    const firstReq = httpMock.expectOne((r) => r.url === '/api/quotes/' && r.params.get('page') === '1');

    state.loadQuotes(2, 10);
    const secondReq = httpMock.expectOne((r) => r.url === '/api/quotes/' && r.params.get('page') === '2');

    secondReq.flush([{ id: 2, author: 'Grace Hopper', text: 'Second', isDeleted: false }]);
    firstReq.flush('boom', { status: 500, statusText: 'Server Error' });

    // The stale error must not overwrite the successful, newer state.
    expect(state.error()).toBeNull();
    expect(state.quotes()).toEqual([{ id: 2, author: 'Grace Hopper', text: 'Second', isDeleted: false }]);
    expect(state.loading()).toBe(false);
  });

  it('nextPage() advances the page and refetches; previousPage() does not go below 1', () => {
    state.loadQuotes(1, 2);
    httpMock
      .expectOne((r) => r.url === '/api/quotes/')
      .flush([
        { id: 1, author: 'A', text: 'a', isDeleted: false },
        { id: 2, author: 'B', text: 'b', isDeleted: false },
      ]);
    expect(state.hasNextPage()).toBe(true);

    state.nextPage();
    expect(state.page()).toBe(2);
    httpMock.expectOne((r) => r.url === '/api/quotes/' && r.params.get('page') === '2').flush([]);
    expect(state.hasNextPage()).toBe(false);

    state.previousPage();
    expect(state.page()).toBe(1); // clamped, never goes below page 1
    httpMock.expectOne((r) => r.url === '/api/quotes/' && r.params.get('page') === '1').flush([]);
  });

  it('changePageSize() resets to page 1 and refetches', () => {
    state.loadQuotes(3, 10);
    httpMock.expectOne((r) => r.url === '/api/quotes/' && r.params.get('page') === '3').flush([]);

    state.changePageSize(20);

    expect(state.page()).toBe(1);
    expect(state.pageSize()).toBe(20);
    httpMock.expectOne((r) => r.url === '/api/quotes/' && r.params.get('size') === '20').flush([]);
  });
});
