using System.Text.Json;
using System.Text.Json.Serialization;
using Fiit.Contracts;
using RabbitMQ.Client;

namespace Fiit.Messaging;

/// <summary>
/// Очереди уведомлений студентам. API публикует в обменник <see cref="Exchange"/>, бот читает <see cref="Queue"/>.
/// Повтор с задержкой — через очереди <see cref="RetryQueue"/> с TTL: истёкшее сообщение возвращается в основную очередь.
/// Необработанные после всех попыток — в <see cref="DeadQueue"/>, разбираются руками.
/// Топологию объявляют и API, и бот (объявление идемпотентно), поэтому порядок запуска не важен и сообщения не теряются.
/// </summary>
public static class NotificationQueues
{
    public const string Exchange = "fiit.notifications";
    public const string Queue = "fiit.bot.notifications";
    public const string DeadQueue = "fiit.bot.notifications.dead";

    /// <summary>Номер попытки доставки (0 — первая).</summary>
    public const string AttemptHeader = "x-attempt";
    public const string ErrorHeader = "x-error";

    /// <summary>Задержки перед повторами: суммарно около 4 часов, дальше — в DeadQueue.</summary>
    public static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(2),
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static string RetryQueue(TimeSpan delay) => $"{Queue}.retry.{(int)delay.TotalSeconds}s";

    /// <summary>Задержка перед следующей попыткой или null — попытки кончились.</summary>
    public static TimeSpan? RetryDelay(int attempt) => attempt >= 0 && attempt < RetryDelays.Length ? RetryDelays[attempt] : null;

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(Queue, Exchange, routingKey: "", cancellationToken: ct);
        await channel.QueueDeclareAsync(DeadQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        foreach (var delay in RetryDelays)
        {
            await channel.QueueDeclareAsync(RetryQueue(delay), durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-message-ttl"] = (int)delay.TotalMilliseconds,
                    ["x-dead-letter-exchange"] = "",
                    ["x-dead-letter-routing-key"] = Queue,
                },
                cancellationToken: ct);
        }
    }

    public static byte[] Serialize(BotNotificationDto notification) => JsonSerializer.SerializeToUtf8Bytes(notification, Json);

    public static BotNotificationDto Deserialize(ReadOnlyMemory<byte> body) =>
        JsonSerializer.Deserialize<BotNotificationDto>(body.Span, Json) ?? throw new JsonException("Empty notification");

    /// <summary>MessageId = id уведомления в outbox: по нему бот отсеивает повторную доставку.</summary>
    public static BasicProperties Properties(BotNotificationDto notification, int attempt = 0) => new()
    {
        Persistent = true,
        ContentType = "application/json",
        MessageId = notification.Id.ToString(),
        Type = notification.Kind.ToString(),
        Headers = new Dictionary<string, object?> { [AttemptHeader] = attempt },
    };

    public static int Attempt(IReadOnlyBasicProperties properties) =>
        properties.Headers?.TryGetValue(AttemptHeader, out var value) == true ? Convert.ToInt32(value) : 0;
}
