import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import type { Observable } from 'rxjs';
import { tap } from 'rxjs';
import type { LoginRequest, LoginResponse } from './auth.model';

// sessionStorage (not localStorage) so tokens don't outlive the browser tab,
// and only the tokens/email are persisted here — never the password.
const ACCESS_TOKEN_KEY = 'quotesApi.accessToken';
const REFRESH_TOKEN_KEY = 'quotesApi.refreshToken';
const EMAIL_KEY = 'quotesApi.email';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly accessTokenSignal = signal<string | null>(
    sessionStorage.getItem(ACCESS_TOKEN_KEY),
  );
  private readonly refreshTokenSignal = signal<string | null>(
    sessionStorage.getItem(REFRESH_TOKEN_KEY),
  );

  // The email used to sign in, shown so the UI can confirm who is logged in.
  readonly email = signal<string | null>(sessionStorage.getItem(EMAIL_KEY));

  readonly isAuthenticated = computed(() => this.accessTokenSignal() !== null);

  // Read by the auth HTTP interceptor to attach the Authorization header.
  accessToken(): string | null {
    return this.accessTokenSignal();
  }

  // POST /api/auth/login — real QuotesApi endpoint (AuthEndpointExtensions).
  // Verifies the password hash server-side and returns a short-lived JWT
  // access token plus a rotating refresh token.
  login(request: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>('/api/auth/login', request).pipe(
      tap((response) => {
        this.accessTokenSignal.set(response.accessToken);
        this.refreshTokenSignal.set(response.refreshToken);
        this.email.set(request.email);
        sessionStorage.setItem(ACCESS_TOKEN_KEY, response.accessToken);
        sessionStorage.setItem(REFRESH_TOKEN_KEY, response.refreshToken);
        sessionStorage.setItem(EMAIL_KEY, request.email);
      }),
    );
  }

  // POST /api/auth/logout — revokes the refresh token server-side. Local
  // state is cleared either way, so the user is signed out client-side even
  // if the request fails.
  logout(): void {
    const refreshToken = this.refreshTokenSignal();

    if (!refreshToken) {
      this.clearLocalState();
      return;
    }

    this.http.post('/api/auth/logout', { refreshToken }).subscribe({
      complete: () => this.clearLocalState(),
      error: () => this.clearLocalState(),
    });
  }

  private clearLocalState(): void {
    this.accessTokenSignal.set(null);
    this.refreshTokenSignal.set(null);
    this.email.set(null);
    sessionStorage.removeItem(ACCESS_TOKEN_KEY);
    sessionStorage.removeItem(REFRESH_TOKEN_KEY);
    sessionStorage.removeItem(EMAIL_KEY);
  }
}
