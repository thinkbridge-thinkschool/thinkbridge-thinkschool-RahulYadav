import type { HttpInterceptorFn } from '@angular/common/http';
import { environment } from '../../environments/environment';

// Rewrites relative '/api/...' requests to the deployed QuotesApi's absolute
// origin in production. Placed after authInterceptor (see app.config.ts) so
// the Authorization header is attached while the URL is still relative —
// authInterceptor's `req.url.startsWith('/api')` check must see it that way.
// In dev, apiBaseUrl is empty and proxy.conf.json handles '/api' instead.
export const apiUrlInterceptor: HttpInterceptorFn = (req, next) => {
  if (!environment.apiBaseUrl || !req.url.startsWith('/api')) {
    return next(req);
  }

  return next(req.clone({ url: `${environment.apiBaseUrl}${req.url}` }));
};
