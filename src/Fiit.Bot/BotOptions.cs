namespace Fiit.Bot;

public class TelegramOptions
{
    public const string Section = "Telegram";

    /// <summary>Токен от @BotFather.</summary>
    public string Token { get; init; } = "";

    /// <summary>Не больше стольких сообщений в секунду на всех (лимит Telegram — около 30).</summary>
    public int MessagesPerSecond { get; init; } = 25;

    /// <summary>
    /// Прокси до api.telegram.org, если Telegram недоступен напрямую: <c>socks5://user:pass@host:port</c> или <c>http://user:pass@host:port</c>.
    /// Пусто — напрямую. Через прокси идут только запросы к Telegram, к API и RabbitMQ — напрямую.
    /// </summary>
    public string? Proxy { get; init; }
}

public class ApiOptions
{
    public const string Section = "Api";

    public string BaseUrl { get; init; } = "http://localhost:5080/";

    /// <summary>Сервисный ключ бота (X-Bot-Key), тот же, что Bot:Key в API.</summary>
    public string BotKey { get; init; } = "";
}
