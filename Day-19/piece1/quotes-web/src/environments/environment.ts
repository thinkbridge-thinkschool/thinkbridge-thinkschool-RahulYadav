// Development default: apiBaseUrl is empty so QuoteService/AuthService's
// relative '/api/...' calls go through proxy.conf.json to the local API.
export const environment = {
  production: false,
  apiBaseUrl: '',
};
