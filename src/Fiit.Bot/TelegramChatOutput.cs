using System.Threading.RateLimiting;
using Fiit.Bot.Core;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Fiit.Bot;

/// <summary>Отправка сообщений бота в Telegram: текст (длинный — частями), кнопки, PDF проверки. Общий лимит скорости на весь бот.</summary>
public sealed class TelegramChatOutput(ITelegramBotClient bot, IBotApi api, IOptions<TelegramOptions> options) : IChatOutput, IDisposable
{
    public const int MaxTextLength = 4096;

    private readonly TokenBucketRateLimiter _limiter = new(new TokenBucketRateLimiterOptions
    {
        TokenLimit = options.Value.MessagesPerSecond,
        TokensPerPeriod = options.Value.MessagesPerSecond,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        QueueLimit = 10_000,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });

    private static readonly ReplyKeyboardMarkup Menu = new([[new KeyboardButton(Dialog.MyTasks)]]) { ResizeKeyboard = true, IsPersistent = true };

    public async Task SendAsync(long chatId, BotMessage message, CancellationToken ct)
    {
        var chunks = Split(message.Text);
        for (var i = 0; i < chunks.Count; i++)
        {
            var last = i == chunks.Count - 1;
            await WaitAsync(ct);
            await bot.SendMessage(chatId, chunks[i], replyMarkup: last ? Markup(message) : null, linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                cancellationToken: ct);
        }
        if (message.Attachment is { } file)
        {
            var bytes = await api.NotificationFileAsync(file.NotificationId, ct);
            await WaitAsync(ct);
            await bot.SendDocument(chatId, InputFile.FromStream(new MemoryStream(bytes), file.FileName), cancellationToken: ct);
        }
    }

    private static ReplyMarkup? Markup(BotMessage message)
    {
        if (message.Rows.Count > 0)
            return new InlineKeyboardMarkup(message.Rows.Select(row => row.Select(b => InlineKeyboardButton.WithCallbackData(b.Text, b.Data))));
        return message.ShowMenu ? Menu : null;
    }

    /// <summary>Режет текст по строкам, чтобы каждая часть влезла в лимит Telegram.</summary>
    internal static List<string> Split(string text)
    {
        var chunks = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            var piece = line;
            while (piece.Length > MaxTextLength)
            {
                Flush();
                chunks.Add(piece[..MaxTextLength]);
                piece = piece[MaxTextLength..];
            }
            if (current.Length + piece.Length + 1 > MaxTextLength)
                Flush();
            if (current.Length > 0)
                current.Append('\n');
            current.Append(piece);
        }
        Flush();
        return chunks.Count == 0 ? [" "] : chunks;

        void Flush()
        {
            if (current.Length > 0)
                chunks.Add(current.ToString());
            current.Clear();
        }
    }

    private async Task WaitAsync(CancellationToken ct)
    {
        using var lease = await _limiter.AcquireAsync(1, ct);
    }

    public void Dispose() => _limiter.Dispose();
}
