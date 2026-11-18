using System.Globalization;
using Fiit.Contracts;

namespace Fiit.Bot.Core;

/// <summary>Время — в часовом поясе Екатеринбурга (UTC+5, Н-5).</summary>
public static class Format
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(5);

    public static string Dt(DateTimeOffset value) => value.ToOffset(Offset).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string Score(decimal? value) => value is { } v ? v.ToString("0.##", CultureInfo.InvariantCulture) : "";

    public static string Status(BotHomeworkStatus s) => s switch
    {
        BotHomeworkStatus.NotSubmitted => "не сдано",
        BotHomeworkStatus.Overdue => "просрочено",
        BotHomeworkStatus.OnReview => "на проверке",
        BotHomeworkStatus.Reviewed => "проверено",
        _ => "",
    };
}
