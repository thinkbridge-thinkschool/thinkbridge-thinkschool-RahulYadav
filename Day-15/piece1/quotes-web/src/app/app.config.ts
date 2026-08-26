import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { authInterceptor } from './core/auth.interceptor';
import { errorInterceptor } from './core/error.interceptor';
import { retryInterceptor } from './core/retry.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    // Order matters: retryInterceptor sits closest to the backend so it can
    // retry the raw HTTP call; errorInterceptor wraps it so it only maps the
    // final, retry-exhausted error into a typed AppError; authInterceptor is
    // outermost since it only touches the outgoing request.
    provideHttpClient(withInterceptors([authInterceptor, errorInterceptor, retryInterceptor])),
  ]
};
