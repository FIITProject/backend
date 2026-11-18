using Fiit.Bot.Core;
using Fiit.Contracts;

namespace Fiit.Web.Services;

public record ChatMessage(bool FromBot, string Text, IReadOnlyList<IReadOnlyList<Button>> Rows, Attachment? Attachment = null)
{
    public DateTime At { get; } = DateTime.Now;
}

/// <summary>Чат одного студента в эмуляторе: лента сообщений.</summary>
public class StudentChat(long telegramUserId, string username)
{
    public long TelegramUserId { get; } = telegramUserId;
    public string Username { get; } = username;
    public List<ChatMessage> Messages { get; } = [];
    public string Title { get; set; } = $"tg {telegramUserId}";
    internal int LastNotificationId { get; set; }
    public BotUser User => new(TelegramUserId, Username);
}

/// <summary>
/// Эмулятор Telegram-бота для теста без Telegram. Диалог — тот же <see cref="Dialog"/>, что в настоящем боте (Fiit.Bot.Core),
/// отличается только «транспорт»: сообщения складываются в ленту на странице, а уведомления читаются из outbox через API, а не из RabbitMQ.
/// </summary>
public class BotEmulator : IChatOutput
{
    private readonly BotApiClient _api;
    private readonly Dialog _dialog;

    public BotEmulator(HttpClient http, BotKey key)
    {
        _api = new BotApiClient(http, () => key.Value);
        _dialog = new Dialog(_api, this, new InMemoryChatStateStore(TimeProvider.System));
    }

    public List<StudentChat> Chats { get; } = [];
    public StudentChat? Current { get; set; }
    public event Action? Changed;

    public StudentChat NewStudent(long? tgId = null, string? username = null)
    {
        var id = tgId ?? Random.Shared.NextInt64(100_000_000, 999_999_999);
        var chat = Chats.FirstOrDefault(c => c.TelegramUserId == id);
        if (chat is null)
        {
            chat = new StudentChat(id, username ?? $"student{id % 10000}");
            Chats.Add(chat);
        }
        Current = chat;
        Changed?.Invoke();
        return chat;
    }

    // ── Действия студента ─────────────────────────────────────────────

    public async Task Start(StudentChat chat, string? code)
    {
        User(chat, string.IsNullOrWhiteSpace(code) ? "/start" : $"/start {code.Trim()}");
        await _dialog.StartAsync(chat.User, code, CancellationToken.None);
        await UpdateTitle(chat);
    }

    public async Task Text(StudentChat chat, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        User(chat, text.Trim());
        await _dialog.TextAsync(chat.User, text, CancellationToken.None);
        await UpdateTitle(chat);
    }

    public async Task Click(StudentChat chat, Button button)
    {
        User(chat, button.Text);
        await _dialog.CallbackAsync(chat.User, button.Data, CancellationToken.None);
        await UpdateTitle(chat);
    }

    public async Task File(StudentChat chat, byte[] bytes, string name)
    {
        User(chat, $"📎 {name} ({bytes.Length / 1024} КБ)");
        var mime = name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" : "application/octet-stream";
        await _dialog.DocumentAsync(chat.User, new IncomingDocument(name, bytes.Length, mime, null, _ => Task.FromResult(bytes)), CancellationToken.None);
    }

    /// <summary>Уведомления, поставленные студентам этой вкладки, — с теми же кнопками, что покажет настоящий бот.</summary>
    public async Task<int> Poll()
    {
        var received = 0;
        foreach (var chat in Chats.ToList())
        {
            var batch = await _api.StudentNotificationsAsync(chat.TelegramUserId, chat.LastNotificationId, CancellationToken.None);
            foreach (var n in batch)
            {
                var message = Dialog.Notification(n);
                chat.Messages.Add(new ChatMessage(true, message.Text, message.Rows, message.Attachment));
                chat.LastNotificationId = n.Id;
            }
            received += batch.Length;
        }
        if (received > 0)
            Changed?.Invoke();
        return received;
    }

    public Task<byte[]> DownloadFile(int notificationId) => _api.NotificationFileAsync(notificationId, CancellationToken.None);

    // ── IChatOutput: ответы диалога ───────────────────────────────────

    Task IChatOutput.SendAsync(long chatId, BotMessage message, CancellationToken ct)
    {
        var chat = Chats.FirstOrDefault(c => c.TelegramUserId == chatId) ?? NewStudent(chatId);
        chat.Messages.Add(new ChatMessage(true, message.Text, message.Rows, message.Attachment));
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    private void User(StudentChat chat, string text)
    {
        chat.Messages.Add(new ChatMessage(false, text, []));
        Changed?.Invoke();
    }

    private async Task UpdateTitle(StudentChat chat)
    {
        try
        {
            var student = await _api.StudentAsync(chat.TelegramUserId, CancellationToken.None);
            if (student.Known)
                chat.Title = $"{student.LastName} {student.FirstName}";
        }
        catch (Exception e) when (e is BotApiException or HttpRequestException)
        {
            // Заголовок чата — косметика
        }
    }
}

/// <summary>Ключ бота (X-Bot-Key), который вводится на странице эмулятора.</summary>
public class BotKey(IConfiguration config)
{
    public string Value { get; set; } = config["BotKey"] ?? "";
}
