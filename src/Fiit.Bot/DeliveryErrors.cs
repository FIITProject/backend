using System.Net;
using Fiit.Bot.Core;
using Telegram.Bot.Exceptions;

namespace Fiit.Bot;

public enum DeliveryErrorKind
{
    /// <summary>Сеть, 5xx, лимит скорости — повторить позже.</summary>
    Transient,
    /// <summary>Повтор не поможет: чат не найден, уведомления нет в API.</summary>
    Permanent,
    /// <summary>Студент заблокировал бота (С-3.2).</summary>
    BotBlocked,
}

public static class DeliveryErrors
{
    public static DeliveryErrorKind Classify(Exception e) => e switch
    {
        ApiRequestException { ErrorCode: 403 } => DeliveryErrorKind.BotBlocked,
        ApiRequestException { ErrorCode: 429 } => DeliveryErrorKind.Transient,
        ApiRequestException { ErrorCode: >= 400 and < 500 } => DeliveryErrorKind.Permanent,
        BotApiException { Status: >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError } => DeliveryErrorKind.Permanent,
        _ => DeliveryErrorKind.Transient,
    };
}
