using Fiit.Contracts;

namespace Fiit.Api.Domain;

public enum NotificationStatus
{
    Pending,
    Sent,
    Failed,
}

/// <summary>Задача на уведомление студента (outbox). Бэкенд создаёт в той же транзакции, бот забирает с арендой и отправляет в Telegram.</summary>
public class Notification
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public NotificationKind Kind { get; set; }
    public required string Text { get; set; }
    public int? HomeworkId { get; set; }
    public string? FileKey { get; set; }
    public string? FileName { get; set; }
    public NotificationStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? LastError { get; set; }
}
