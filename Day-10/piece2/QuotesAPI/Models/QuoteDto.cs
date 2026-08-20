namespace QuotesApi.Models;

public sealed class QuoteDto
{
    public int Id { get; init; }

    public string Author { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;
}