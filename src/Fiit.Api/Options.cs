namespace Fiit.Api;

public class BotOptions
{
    public const string Section = "Bot";

    /// <summary>Сервисный ключ бота (X-Bot-Key).</summary>
    public string Key { get; init; } = "";

    /// <summary>Имя бота для ссылок-приглашений t.me/&lt;Username&gt;?start=&lt;код&gt;.</summary>
    public string Username { get; init; } = "fiit_hw_bot";

    public string InviteLink(string code) => $"https://t.me/{Username}?start={code}";
}

public static class Limits
{
    /// <summary>Максимальный размер PDF сдачи (С-5.2).</summary>
    public const long MaxPdfBytes = 20 * 1024 * 1024;
}
