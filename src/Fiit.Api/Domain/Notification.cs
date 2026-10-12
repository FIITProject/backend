namespace Fiit.Api.Domain;

public enum NotificationKind
{
    HomeworkPublished,
    HomeworkChanged,
    ReviewSent,
}

public enum NotificationStatus
{
    Pending,
    Sent,
    Failed,
}

/// <summary>Задача на уведомление студента (outbox). Бэкенд создаёт, бот забирает с арендой и отправляет в Telegram.</summary>
public class Notification
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public NotificationKind Kind { get; set; }
    public int RefId { get; set; }
    public NotificationStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? LastError { get; set; }
}
