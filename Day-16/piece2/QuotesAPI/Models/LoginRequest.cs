namespace QuotesApi.Models;

public record LoginRequest(
    string Email,
    string Password
);