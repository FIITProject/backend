using Fiit.Api.Data;
using Fiit.Api.Domain;
using Fiit.Contracts;
using Fiit.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Fiit.Api.Services;

/// <summary>
/// Relay transactional outbox → RabbitMQ. Берёт пачку Pending (FOR UPDATE SKIP LOCKED — несколько экземпляров API не мешают друг другу),
/// публикует с подтверждением брокера и помечает Published. Упал между публикацией и коммитом — сообщение уйдёт ещё раз,
/// бот отсеет его по статусу (доставка «хотя бы один раз»).
/// </summary>
public class NotificationOutboxPublisher(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    TimeProvider time,
    ILogger<NotificationOutboxPublisher> log) : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var factory = new ConnectionFactory { Uri = new Uri(options.Value.Uri), ClientProvidedName = "fiit-api-outbox" };
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var connection = await factory.CreateConnectionAsync(stop);
                await using var channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), stop);
                await NotificationQueues.DeclareAsync(channel, stop);
                log.LogInformation("Outbox publisher connected to RabbitMQ");
                while (!stop.IsCancellationRequested)
                {
                    if (await PublishBatchAsync(channel, stop) < BatchSize)
                        await Task.Delay(PollInterval, time, stop);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                log.LogError(e, "Outbox publisher failed, reconnecting in {Delay}", ReconnectDelay);
                try
                {
                    await Task.Delay(ReconnectDelay, time, stop);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task<int> PublishBatchAsync(IChannel channel, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var pending = nameof(NotificationStatus.Pending);
        var batch = await db.Notifications
            .FromSql($"SELECT * FROM notifications WHERE status = {pending} ORDER BY id LIMIT {BatchSize} FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);
        if (batch.Count == 0)
            return 0;

        var studentIds = batch.Select(n => n.StudentId).Distinct().ToList();
        var telegram = await db.Students.Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.TelegramUserId, ct);
        try
        {
            foreach (var n in batch)
            {
                var message = ToDto(n, telegram[n.StudentId]);
                // mandatory: если очередь не привязана, брокер вернёт сообщение — это ошибка, а не тихая потеря
                await channel.BasicPublishAsync(NotificationQueues.Exchange, "", mandatory: true,
                    NotificationQueues.Properties(message), NotificationQueues.Serialize(message), ct);
                n.Status = NotificationStatus.Published;
                n.PublishedAt = time.GetUtcNow();
            }
        }
        finally
        {
            // Фиксируем то, что успели опубликовать, даже если на середине пачки упали
            await db.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
        }
        log.LogDebug("Published {Count} notifications", batch.Count);
        return batch.Count;
    }

    public static BotNotificationDto ToDto(Notification n, long telegramUserId) =>
        new(n.Id, telegramUserId, n.Kind, n.Text, n.HomeworkId, n.FileKey is not null, n.FileName, n.CreatedAt);
}
