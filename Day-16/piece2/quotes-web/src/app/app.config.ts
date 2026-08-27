import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withViewTransitions } from '@angular/router';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth.interceptor';
import { errorInterceptor } from './core/error.interceptor';
import { retryInterceptor } from './core/retry.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    // withComponentInputBinding: binds the ':id' path param straight onto
    // QuoteDetail's `id` input, no ActivatedRoute plumbing in the component.
    // withViewTransitions: wraps navigations in the View Transition API
    // (falls back to a plain navigation in browsers that don't support it).
    provideRouter(routes, withComponentInputBinding(), withViewTransitions()),
    // Order matters: retryInterceptor sits closest to the backend so it can
    // retry the raw HTTP call; errorInterceptor wraps it so it only maps the
    // final, retry-exhausted error into a typed AppError; authInterceptor is
    // outermost since it only touches the outgoing request.
    provideHttpClient(withInterceptors([authInterceptor, errorInterceptor, retryInterceptor])),
  ]
};
