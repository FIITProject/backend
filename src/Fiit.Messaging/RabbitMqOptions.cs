namespace Fiit.Messaging;

public class RabbitMqOptions
{
    public const string Section = "RabbitMq";

    /// <summary>amqp://user:password@host:5672/vhost. Пусто — RabbitMQ не используется (уведомления копятся в outbox).</summary>
    public string Uri { get; init; } = "";

    public bool Enabled => !string.IsNullOrWhiteSpace(Uri);
}
