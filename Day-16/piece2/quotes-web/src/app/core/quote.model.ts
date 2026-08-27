// Mirrors QuotesApi.Models.Quote (System.Text.Json camelCase serialization).
export interface Quote {
  id: number;
  author: string;
  text: string;
  isDeleted: boolean;
}

// Mirrors the anonymous CreateQuoteRequest record in QuoteEndpointExtensions
// (POST /api/quotes). The API derives the user id from the auth token, so
// there is no userId field here.
export interface QuoteCreateRequest {
  author: string;
  text: string;
}
