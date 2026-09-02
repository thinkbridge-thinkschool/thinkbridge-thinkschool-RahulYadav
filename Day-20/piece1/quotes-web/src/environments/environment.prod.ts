// Production: the Angular app (Static Web Apps) and QuotesApi (Azure
// Container Apps) are served from different origins, so relative '/api/...'
// calls need an absolute base URL. This is the real, verified Week-1
// QuotesApi container app — not a placeholder.
export const environment = {
  production: true,
  apiBaseUrl: 'https://quotes-api-final.proudpebble-45156de0.centralindia.azurecontainerapps.io',
};
