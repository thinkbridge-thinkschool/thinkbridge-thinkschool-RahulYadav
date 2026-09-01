// Mirrors QuotesApi.Models.LoginRequest (System.Text.Json camelCase serialization).
export interface LoginRequest {
  email: string;
  password: string;
}

// Mirrors QuotesApi.Models.LoginResponse, returned by POST /api/auth/login
// and POST /api/auth/refresh.
export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
}
