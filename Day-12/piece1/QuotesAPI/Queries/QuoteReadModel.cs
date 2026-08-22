namespace QuotesApi.Queries;

public class QuoteReadModel
{
    public int Id { get; set; }

    public string Author { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}