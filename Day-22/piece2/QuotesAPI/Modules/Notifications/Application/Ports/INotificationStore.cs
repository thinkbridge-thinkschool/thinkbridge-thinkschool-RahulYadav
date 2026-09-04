using QuotesApi.Modules.Notifications.Contracts;

namespace QuotesApi.Modules.Notifications.Application.Ports;

public interface INotificationStore
{
    Task RecordAsync(
        string eventType,
        string message,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationDto>> GetRecentAsync(int count, CancellationToken cancellationToken);
}
