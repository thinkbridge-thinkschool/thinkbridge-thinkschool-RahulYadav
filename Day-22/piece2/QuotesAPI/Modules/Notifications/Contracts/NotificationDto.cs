namespace QuotesApi.Modules.Notifications.Contracts;

public sealed record NotificationDto(
    int Id,
    string EventType,
    string Message,
    DateTimeOffset CreatedAtUtc);
