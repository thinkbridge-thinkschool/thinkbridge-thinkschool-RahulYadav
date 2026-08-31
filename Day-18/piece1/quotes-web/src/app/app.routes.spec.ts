import { routes } from './app.routes';
import { authGuard } from './core/auth.guard';

describe('routes', () => {
  it('redirects the empty path to /quotes', () => {
    const root = routes.find((r) => r.path === '');
    expect(root?.redirectTo).toBe('quotes');
    expect(root?.pathMatch).toBe('full');
  });

  it('defines /quotes with nested :id and new children', () => {
    const quotes = routes.find((r) => r.path === 'quotes');
    expect(quotes).toBeTruthy();
    expect(quotes?.loadComponent).toBeInstanceOf(Function);

    const detail = quotes?.children?.find((r) => r.path === ':id');
    expect(detail).toBeTruthy();

    const create = quotes?.children?.find((r) => r.path === 'new');
    expect(create).toBeTruthy();
  });

  it('lazy-loads the quote detail route rather than referencing a component directly', () => {
    const quotes = routes.find((r) => r.path === 'quotes');
    const detail = quotes?.children?.find((r) => r.path === ':id');

    expect(detail?.loadComponent).toBeInstanceOf(Function);
    expect(detail?.component).toBeUndefined();
  });

  it('resolves the lazy-loaded detail route to the real QuoteDetail component', async () => {
    const quotes = routes.find((r) => r.path === 'quotes');
    const detail = quotes?.children?.find((r) => r.path === ':id');

    const loaded = await detail!.loadComponent!();
    const { QuoteDetail } = await import('./quote-detail/quote-detail');
    expect(loaded).toBe(QuoteDetail);
  });

  it('protects the /quotes/new route with authGuard', () => {
    const quotes = routes.find((r) => r.path === 'quotes');
    const create = quotes?.children?.find((r) => r.path === 'new');

    expect(create?.canActivate).toContain(authGuard);
  });

  it('sends unmatched paths back to /quotes', () => {
    const wildcard = routes.find((r) => r.path === '**');
    expect(wildcard?.redirectTo).toBe('quotes');
  });
});
