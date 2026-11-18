namespace Fiit.Bot.Core;

/// <summary>Кнопка под сообщением. Data — команда для <see cref="Dialog.CallbackAsync"/> (в Telegram — callback_data, до 64 байт).</summary>
public record Button(string Text, string Data);

/// <summary>PDF, который надо приложить к сообщению: качается через /api/bot/notifications/{id}/file.</summary>
public record Attachment(int NotificationId, string FileName);

/// <summary>Сообщение бота. Rows — ряды кнопок; ShowMenu — показать постоянную кнопку «Мои задания» под полем ввода.</summary>
public record BotMessage(string Text, IReadOnlyList<IReadOnlyList<Button>> Rows, bool ShowMenu = false, Attachment? Attachment = null)
{
    public static BotMessage Plain(string text) => new(text, []);

    /// <summary>Каждая кнопка — в своём ряду (так читаются длинные названия ДЗ).</summary>
    public static BotMessage Column(string text, IEnumerable<Button> buttons) => new(text, buttons.Select(b => (IReadOnlyList<Button>)[b]).ToList());

    public static BotMessage Row(string text, params Button[] buttons) => new(text, buttons.Length == 0 ? [] : [buttons]);
}

/// <summary>Куда отправлять сообщения: Telegram или лента эмулятора на сайте.</summary>
public interface IChatOutput
{
    Task SendAsync(long chatId, BotMessage message, CancellationToken ct);
}

public record BotUser(long Id, string? Username);

/// <summary>Файл, присланный студентом. Download — скачать содержимое (в Telegram — через getFile, только когда файл действительно нужен).</summary>
public record IncomingDocument(string? FileName, long? Size, string? MimeType, string? MediaGroupId, Func<CancellationToken, Task<byte[]>> Download);
