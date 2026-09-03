using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace QuotesApi.Data;

// Day 21: EF Core DbCommandInterceptor that records every real database
// command SQLite executes. Hooked into DbContextOptionsBuilder in
// Extensions/InfrastructureExtensions.cs, sharing one DbQueryCounter
// singleton with the /api/diagnostics/cache endpoint.
public sealed class DbQueryCounterInterceptor : DbCommandInterceptor
{
    private readonly DbQueryCounter _counter;

    public DbQueryCounterInterceptor(DbQueryCounter counter)
    {
        _counter = counter;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        _counter.RecordCommand(command.CommandText, eventData.Duration);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        _counter.RecordCommand(command.CommandText, eventData.Duration);
        return base.ReaderExecuted(command, eventData, result);
    }
}
