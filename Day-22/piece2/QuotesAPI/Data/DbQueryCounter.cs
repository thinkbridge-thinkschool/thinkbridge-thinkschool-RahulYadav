namespace QuotesApi.Data;

// Day 21: real EF Core database-command instrumentation, fed by
// DbQueryCounterInterceptor. Counts actual commands sent to SQLite, not
// application/repository method calls — a cache hit never reaches this
// class at all, since the HybridCache factory (and therefore the
// repository/DbContext) simply never runs for a hit.
public sealed class DbQueryCounter
{
    private long _totalCommands;
    private long _quoteReadCommands;
    private long _totalElapsedTicks;

    public void RecordCommand(string commandText, TimeSpan elapsed)
    {
        Interlocked.Increment(ref _totalCommands);
        Interlocked.Add(ref _totalElapsedTicks, elapsed.Ticks);

        if (IsQuoteReadCommand(commandText))
        {
            Interlocked.Increment(ref _quoteReadCommands);
        }
    }

    private static bool IsQuoteReadCommand(string commandText) =>
        commandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase) &&
        commandText.Contains("\"Quotes\"", StringComparison.OrdinalIgnoreCase);

    public long TotalCommands => Interlocked.Read(ref _totalCommands);

    public long QuoteReadCommands => Interlocked.Read(ref _quoteReadCommands);

    public TimeSpan TotalElapsed => TimeSpan.FromTicks(Interlocked.Read(ref _totalElapsedTicks));

    public void Reset()
    {
        Interlocked.Exchange(ref _totalCommands, 0);
        Interlocked.Exchange(ref _quoteReadCommands, 0);
        Interlocked.Exchange(ref _totalElapsedTicks, 0);
    }
}
