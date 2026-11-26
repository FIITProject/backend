using Fiit.Bot;
using Fiit.Bot.Core;
using Fiit.Messaging;
using Microsoft.Extensions.Options;
using Telegram.Bot;

// Telegram-бот студентов — отдельный процесс: long polling Telegram, очередь уведомлений RabbitMQ, бэкенд через /api/bot/*.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<TelegramOptions>().BindConfiguration(TelegramOptions.Section)
    .Validate(o => !string.IsNullOrWhiteSpace(o.Token), "Telegram:Token is required (token from @BotFather)").ValidateOnStart();
builder.Services.AddOptions<ApiOptions>().BindConfiguration(ApiOptions.Section)
    .Validate(o => !string.IsNullOrWhiteSpace(o.BotKey), "Api:BotKey is required").ValidateOnStart();
builder.Services.AddOptions<RabbitMqOptions>().BindConfiguration(RabbitMqOptions.Section)
    .Validate(o => o.Enabled, "RabbitMq:Uri is required").ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var o = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
    var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
    if (!string.IsNullOrWhiteSpace(o.Proxy))
    {
        handler.Proxy = TelegramProxy.Create(o.Proxy);
        handler.UseProxy = true;
    }
    return new TelegramBotClient(o.Token, new HttpClient(handler));
});
builder.Services.AddSingleton<IBotApi>(sp =>
{
    var o = sp.GetRequiredService<IOptions<ApiOptions>>().Value;
    // Сдача PDF до 20 МБ — запас по времени; соединения переоткрываются, чтобы подхватывать смену DNS
    var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
        BaseAddress = new Uri(o.BaseUrl),
        Timeout = TimeSpan.FromMinutes(2),
    };
    return new BotApiClient(http, () => o.BotKey);
});
builder.Services.AddSingleton<IChatStateStore, InMemoryChatStateStore>();
builder.Services.AddSingleton<IChatOutput, TelegramChatOutput>();
builder.Services.AddSingleton<Dialog>();
builder.Services.AddHostedService<TelegramUpdates>();
builder.Services.AddHostedService<NotificationConsumer>();

using var host = builder.Build();
try
{
    await host.RunAsync();
    return 0;
}
catch (Exception)
{
    // Причину хост уже записал в лог («Hosting failed to start»). Выходим кодом 1, а не необработанным исключением:
    // dotnet как PID 1 в контейнере на abort() может зависнуть вместо выхода
    return 1;
}
