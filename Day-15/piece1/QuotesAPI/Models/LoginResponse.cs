namespace QuotesApi.Models;

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn
);