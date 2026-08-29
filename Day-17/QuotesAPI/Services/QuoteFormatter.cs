namespace QuotesApi.Services;

public class QuoteFormatter
{
    public string Format(string text)
    {
        return text.Trim();
    }
}