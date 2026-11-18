using Fiit.Contracts;

namespace Fiit.Api.Domain;

/// <summary>
/// Уведомление студенту (transactional outbox). Создаётся в той же транзакции, что и событие;
/// фоновый публикатор передаёт его в RabbitMQ, бот доставляет в Telegram и сообщает результат.
/// </summary>
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
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? LastError { get; set; }
}
