namespace QuotesApi.Modules.Notifications.Infrastructure.Persistence;

// Deliberately anemic: Notifications has no business rules of its own, only
// a log of reactions to other modules' events, so an EF entity is enough —
// no aggregate is warranted here (contrast with Collections.Domain.Collection).
public sealed class NotificationRecord
{
    public int Id { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
