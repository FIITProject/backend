using System.Net;
using Fiit.Bot.Core;
using Fiit.Contracts;
using Fiit.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telegram.Bot.Exceptions;

namespace Fiit.Bot.Tests;

public class DeliveryTests
{
    private readonly FakeApi _api = new();
    private readonly RecordingOutput _out = new();
    private readonly NotificationConsumer _consumer;
    private static readonly BotNotificationDto Notice = new(7, 42, NotificationKind.HomeworkPublished, "Новое ДЗ", 5, false, null, DateTimeOffset.UtcNow);

    public DeliveryTests() =>
        _consumer = new NotificationConsumer(_api, _out, Options.Create(new RabbitMqOptions()), NullLogger<NotificationConsumer>.Instance);

    [Theory]
    [InlineData(403, DeliveryErrorKind.BotBlocked)]
    [InlineData(400, DeliveryErrorKind.Permanent)]
    [InlineData(429, DeliveryErrorKind.Transient)]
    [InlineData(502, DeliveryErrorKind.Transient)]
    public void Telegram_errors(int code, DeliveryErrorKind kind) =>
        Assert.Equal(kind, DeliveryErrors.Classify(new ApiRequestException("error", code)));

    [Fact]
    public void Network_and_backend_errors()
    {
        Assert.Equal(DeliveryErrorKind.Transient, DeliveryErrors.Classify(new HttpRequestException("connection refused")));
        Assert.Equal(DeliveryErrorKind.Transient, DeliveryErrors.Classify(new BotApiException(HttpStatusCode.ServiceUnavailable, "", null)));
        Assert.Equal(DeliveryErrorKind.Permanent, DeliveryErrors.Classify(new BotApiException(HttpStatusCode.NotFound, "", null)));
    }

    [Fact]
    public async Task Delivers_and_reports()
    {
        var outcome = await _consumer.DeliverAsync(Notice, 0, CancellationToken.None);

        Assert.Equal(new NotificationConsumer.Outcome(), outcome);
        var (chat, message) = Assert.Single(_out.Sent);
        Assert.Equal((42L, "Новое ДЗ"), (chat, message.Text));
        Assert.Equal([7], _api.Delivered);
    }

    [Fact]
    public async Task Already_delivered_is_skipped()
    {
        _api.NotificationStates[7] = NotificationStatus.Delivered;
        await _consumer.DeliverAsync(Notice, 0, CancellationToken.None);
        Assert.Empty(_out.Sent);
    }

    [Fact]
    public async Task Transient_error_is_retried_with_growing_delay_then_dead()
    {
        _out.Fail = (_, _) => new ApiRequestException("Too Many Requests", 429);

        Assert.Equal(NotificationQueues.RetryDelays[0], (await _consumer.DeliverAsync(Notice, 0, CancellationToken.None)).Retry);
        Assert.Equal(NotificationQueues.RetryDelays[3], (await _consumer.DeliverAsync(Notice, 3, CancellationToken.None)).Retry);
        Assert.Empty(_api.Failed);

        var last = await _consumer.DeliverAsync(Notice, NotificationQueues.RetryDelays.Length, CancellationToken.None);
        Assert.True(last.Dead);
        Assert.False(Assert.Single(_api.Failed).Request.BotBlocked);
    }

    [Fact]
    public async Task Blocked_bot_is_reported_without_retry()
    {
        _out.Fail = (_, _) => new ApiRequestException("Forbidden: bot was blocked by the user", 403);

        var outcome = await _consumer.DeliverAsync(Notice, 0, CancellationToken.None);

        Assert.Equal((null, false), (outcome.Retry, outcome.Dead));
        Assert.True(Assert.Single(_api.Failed).Request.BotBlocked);
    }

    [Fact]
    public void Long_text_is_split_by_lines()
    {
        var text = string.Join("\n", Enumerable.Range(0, 500).Select(i => $"Строка номер {i:D4} с описанием задания"));
        var chunks = TelegramChatOutput.Split(text);
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= TelegramChatOutput.MaxTextLength));
        Assert.Equal(text, string.Join("\n", chunks));
    }
}
