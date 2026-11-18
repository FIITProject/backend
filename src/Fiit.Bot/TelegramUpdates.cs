using System.Collections.Concurrent;
using Fiit.Bot.Core;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Fiit.Bot;

/// <summary>
/// Long polling Telegram. Апдейты разных студентов обрабатываются параллельно, одного студента — строго по очереди
/// (иначе два быстрых нажатия могли бы перепутать шаги анкеты).
/// </summary>
public class TelegramUpdates(ITelegramBotClient bot, Dialog dialog, ILogger<TelegramUpdates> log) : BackgroundService, IUpdateHandler
{
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _locks = new();

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var me = await bot.GetMe(stop);
        log.LogInformation("Bot @{Username} started", me.Username);
        await bot.SetMyCommands(
        [
            new BotCommand { Command = "tasks", Description = "Мои задания" },
            new BotCommand { Command = "start", Description = "Запись по приглашению" },
            new BotCommand { Command = "help", Description = "Справка" },
        ], cancellationToken: stop);
        await bot.ReceiveAsync(this, new ReceiverOptions { AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery] }, stop);
    }

    public Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken ct)
    {
        var userId = update.Message?.From?.Id ?? update.CallbackQuery?.From.Id;
        if (userId is null)
            return Task.CompletedTask;
        _ = Task.Run(async () =>
        {
            var gate = _locks.GetOrAdd(userId.Value, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                await ProcessAsync(update, ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                log.LogError(e, "Failed to handle update {UpdateId} from {UserId}", update.Id, userId);
            }
            finally
            {
                gate.Release();
            }
        }, ct);
        return Task.CompletedTask;
    }

    public Task HandleErrorAsync(ITelegramBotClient client, Exception exception, HandleErrorSource source, CancellationToken ct)
    {
        log.LogWarning(exception, "Telegram polling error ({Source})", source);
        // Сеть или Telegram недоступны — не долбим API в цикле
        return Task.Delay(TimeSpan.FromSeconds(5), ct);
    }

    private async Task ProcessAsync(Update update, CancellationToken ct)
    {
        if (update.CallbackQuery is { } callback)
        {
            try
            {
                await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                // Старое нажатие (бот лежал дольше, чем живёт callback) — ответить нельзя, но действие выполняем
                log.LogDebug(e, "Failed to answer callback {Id}", callback.Id);
            }
            if (callback.Message?.Chat.Type == ChatType.Private && callback.Data is { } data)
                await dialog.CallbackAsync(new BotUser(callback.From.Id, callback.From.Username), data, ct);
            return;
        }

        if (update.Message is not { From: { } from } message)
            return;
        if (message.Chat.Type != ChatType.Private)
            return; // в группах бот молчит: анкета и сдача — только в личке
        var user = new BotUser(from.Id, from.Username);

        if (message.Document is { } doc)
        {
            await dialog.DocumentAsync(user, new IncomingDocument(doc.FileName, doc.FileSize, doc.MimeType, message.MediaGroupId, async token =>
            {
                using var ms = new MemoryStream();
                await bot.GetInfoAndDownloadFile(doc.FileId, ms, token);
                return ms.ToArray();
            }), ct);
            return;
        }
        if (message.Photo is not null)
        {
            await dialog.DocumentAsync(user, new IncomingDocument("photo.jpg", null, "image/jpeg", message.MediaGroupId, _ => Task.FromResult<byte[]>([])), ct);
            return;
        }
        if (message.Text is { } text)
            await dialog.TextAsync(user, text, ct);
    }
}
