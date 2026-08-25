namespace QuotesApi.Models;

public sealed class Quote
{
    private Quote(string author, string text)
    {
        Author = author;
        Text = text;
    }

    private Quote()
    {
    }

    public const int MinTextLength = 1;
    public const int MaxTextLength = 1000;
    public const int MinAuthorLength = 1;
    public const int MaxAuthorLength = 200;

    public int Id { get; private set; }

    public string Author { get; private set; } = string.Empty;

    public string Text { get; private set; } = string.Empty;

    public bool IsDeleted { get; private set; }

    public static QuoteCreationResult Create(string? author, string? text)
    {
        if (string.IsNullOrWhiteSpace(author))
            return QuoteCreationResult.Failure("Author is required.");

        author = author.Trim();
        if (author.Length < MinAuthorLength || author.Length > MaxAuthorLength)
            return QuoteCreationResult.Failure($"Author must be {MinAuthorLength}-{MaxAuthorLength} characters.");

        if (string.IsNullOrWhiteSpace(text))
            return QuoteCreationResult.Failure("Text is required.");

        text = text.Trim();
        if (text.Length < MinTextLength || text.Length > MaxTextLength)
            return QuoteCreationResult.Failure($"Text must be {MinTextLength}-{MaxTextLength} characters.");

        return QuoteCreationResult.Success(new Quote(author, text));
    }

    public void SoftDelete() => IsDeleted = true;
}

public sealed class QuoteCreationResult
{
    private QuoteCreationResult(bool isSuccess, Quote? quote, string? error)
    {
        IsSuccess = isSuccess;
        Quote = quote;
        Error = error;
    }

    public bool IsSuccess { get; }

    public Quote? Quote { get; }

    public string? Error { get; }

    public static QuoteCreationResult Success(Quote quote) =>
        new(true, quote, null);

    public static QuoteCreationResult Failure(string error) =>
        new(false, null, error);
}