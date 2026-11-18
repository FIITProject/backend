namespace Fiit.Bot;

public class TelegramOptions
{
    public const string Section = "Telegram";

    /// <summary>Токен от @BotFather.</summary>
    public string Token { get; init; } = "";

    /// <summary>Не больше стольких сообщений в секунду на всех (лимит Telegram — около 30).</summary>
    public int MessagesPerSecond { get; init; } = 25;
}

public class ApiOptions
{
    public const string Section = "Api";

    public string BaseUrl { get; init; } = "http://localhost:5080/";

    /// <summary>Сервисный ключ бота (X-Bot-Key), тот же, что Bot:Key в API.</summary>
    public string BotKey { get; init; } = "";
}
