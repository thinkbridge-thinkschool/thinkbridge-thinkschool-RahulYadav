import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

// Top-level layout: QuotesPage owns the list + a nested outlet for whichever
// detail/action view is active (a quote's detail, or the create form), so
// the existing master-detail UI is preserved.
//
// Only the detail route is required to be lazy — it's loadComponent()'d
// below so its code is fetched on navigation to /quotes/:id, not bundled
// into the initial load. Login and the create form are lazy too since
// neither is needed until a user actually asks for them.
export const routes: Routes = [
  { path: '', redirectTo: 'quotes', pathMatch: 'full' },
  {
    path: 'login',
    loadComponent: () => import('./login/login').then((m) => m.Login),
  },
  {
    path: 'quotes',
    loadComponent: () => import('./quotes-page/quotes-page').then((m) => m.QuotesPage),
    children: [
      {
        // Protected: matches the real API's POST /api/quotes, which requires
        // an authenticated user (QuoteEndpointExtensions.cs).
        path: 'new',
        canActivate: [authGuard],
        loadComponent: () => import('./quote-create/quote-create').then((m) => m.QuoteCreate),
      },
      {
        // The real quote id (GET /api/quotes/{id}), lazy-loaded.
        path: ':id',
        loadComponent: () => import('./quote-detail/quote-detail').then((m) => m.QuoteDetail),
      },
    ],
  },
  { path: '**', redirectTo: 'quotes' },
];
