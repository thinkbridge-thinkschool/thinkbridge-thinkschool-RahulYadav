using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Modules.Notifications.Application.Ports;
using QuotesApi.Modules.Notifications.Contracts;
using QuotesApi.Modules.Notifications.Infrastructure.Persistence;

namespace QuotesApi.Modules.Notifications.Infrastructure;

internal sealed class NotificationStore : INotificationStore
{
    private readonly QuotesDbContext _db;

    public NotificationStore(QuotesDbContext db)
    {
        _db = db;
    }

    public async Task RecordAsync(
        string eventType,
        string message,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        _db.Notifications.Add(new NotificationRecord
        {
            EventType = eventType,
            Message = message,
            CreatedAtUtc = createdAtUtc
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationDto>> GetRecentAsync(
        int count,
        CancellationToken cancellationToken) =>
        await _db.Notifications
            .AsNoTracking()
            // Ordering by Id (not CreatedAtUtc) is deliberate: SQLite cannot
            // translate ORDER BY over a DateTimeOffset column, and Id is
            // already monotonic with insertion order, so it gives the same
            // "most recent first" result without pulling every row into
            // memory to sort client-side.
            .OrderByDescending(x => x.Id)
            .Take(count)
            .Select(x => new NotificationDto(x.Id, x.EventType, x.Message, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);
}
