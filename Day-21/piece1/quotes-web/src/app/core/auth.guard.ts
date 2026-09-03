import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

// Protects routes that need a signed-in user, mirroring the real QuotesApi's
// own requirement: POST /api/quotes is `.RequireAuthorization()` (see
// QuoteEndpointExtensions.cs), so the /quotes/new route that submits that
// request is guarded the same way here.
//
// Returning a UrlTree (instead of calling router.navigateByUrl and returning
// false) lets the Router treat this as a redirect it performs itself, which
// is the documented, preferred pattern for functional guards.
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};
