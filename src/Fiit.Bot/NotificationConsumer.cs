using System.Text.Json;
using Fiit.Bot.Core;
using Fiit.Contracts;
using Fiit.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Fiit.Bot;

/// <summary>
/// Читает очередь уведомлений и доставляет их в Telegram (Н-7).
/// Уже доставленное (повторная публикация или повторная доставка) пропускается по статусу в API.
/// Временная ошибка — копия уходит в очередь повтора с задержкой; постоянная или попытки кончились — в dead-очередь, API узнаёт о неудаче.
/// </summary>
public class NotificationConsumer(
    IBotApi api,
    IChatOutput output,
    IOptions<RabbitMqOptions> options,
    ILogger<NotificationConsumer> log) : BackgroundService
{
    private const ushort Prefetch = 8;
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var factory = new ConnectionFactory { Uri = new Uri(options.Value.Uri), ClientProvidedName = "fiit-bot" };
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var connection = await factory.CreateConnectionAsync(stop);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stop);
                await using var publisher = await connection.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), stop);
                await NotificationQueues.DeclareAsync(channel, stop);
                await channel.BasicQosAsync(0, Prefetch, false, stop);

                var closed = new TaskCompletionSource();
                connection.ConnectionShutdownAsync += (_, _) =>
                {
                    closed.TrySetResult();
                    return Task.CompletedTask;
                };
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, ea) => HandleAsync(channel, publisher, ea, stop);
                await channel.BasicConsumeAsync(NotificationQueues.Queue, autoAck: false, consumer, stop);
                log.LogInformation("Consuming {Queue}", NotificationQueues.Queue);
                await closed.Task.WaitAsync(stop);
                log.LogWarning("RabbitMQ connection closed, reconnecting");
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                log.LogError(e, "RabbitMQ consumer failed, reconnecting in {Delay}", ReconnectDelay);
            }
            try
            {
                await Task.Delay(ReconnectDelay, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task HandleAsync(IChannel channel, IChannel publisher, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        BotNotificationDto notification;
        try
        {
            notification = NotificationQueues.Deserialize(ea.Body);
        }
        catch (JsonException e)
        {
            log.LogError(e, "Malformed notification message {MessageId}", ea.BasicProperties.MessageId);
            await publisher.BasicPublishAsync("", NotificationQueues.DeadQueue, mandatory: true,
                new BasicProperties { Persistent = true, Headers = new Dictionary<string, object?> { [NotificationQueues.ErrorHeader] = e.Message } }, ea.Body, ct);
            await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
            return;
        }

        var attempt = NotificationQueues.Attempt(ea.BasicProperties);
        var outcome = await DeliverAsync(notification, attempt, ct);
        try
        {
            if (outcome is { Retry: { } delay })
            {
                await publisher.BasicPublishAsync("", NotificationQueues.RetryQueue(delay), mandatory: true,
                    NotificationQueues.Properties(notification, attempt + 1), ea.Body, ct);
            }
            else if (outcome is { Dead: true })
            {
                var props = NotificationQueues.Properties(notification, attempt);
                props.Headers![NotificationQueues.ErrorHeader] = outcome.Error;
                await publisher.BasicPublishAsync("", NotificationQueues.DeadQueue, mandatory: true, props, ea.Body, ct);
            }
            await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            // Не смогли переложить — вернём в очередь как есть, попробуем снова
            log.LogError(e, "Failed to reschedule notification {Id}", notification.Id);
            await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: true, ct);
        }
    }

    internal record Outcome(TimeSpan? Retry = null, bool Dead = false, string? Error = null);

    internal async Task<Outcome> DeliverAsync(BotNotificationDto notification, int attempt, CancellationToken ct)
    {
        try
        {
            var state = await api.NotificationStateAsync(notification.Id, ct);
            if (state.Status == NotificationStatus.Delivered)
            {
                log.LogInformation("Notification {Id} already delivered, skipping", notification.Id);
                return new Outcome();
            }
            await output.SendAsync(notification.TelegramUserId, Dialog.Notification(notification), ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            var kind = DeliveryErrors.Classify(e);
            var retry = kind == DeliveryErrorKind.Transient ? NotificationQueues.RetryDelay(attempt) : null;
            if (retry is not null)
            {
                log.LogWarning(e, "Notification {Id} attempt {Attempt} failed, retry in {Delay}", notification.Id, attempt + 1, retry);
                return new Outcome(Retry: retry);
            }
            log.LogError(e, "Notification {Id} not delivered ({Kind})", notification.Id, kind);
            await ReportAsync(() => api.NotificationFailedAsync(notification.Id,
                new BotDeliveryFailedRequest(e.Message, kind == DeliveryErrorKind.BotBlocked), ct), notification.Id);
            return new Outcome(Dead: kind != DeliveryErrorKind.BotBlocked, Error: e.Message);
        }

        await ReportAsync(() => api.NotificationDeliveredAsync(notification.Id, ct), notification.Id);
        return new Outcome();
    }

    /// <summary>Статус в API — для отсева повторов и значка «бот заблокирован»; если API сейчас недоступен, доставку это не отменяет.</summary>
    private async Task ReportAsync(Func<Task> report, int id)
    {
        try
        {
            await report();
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Failed to report delivery result of notification {Id}", id);
        }
    }
}
