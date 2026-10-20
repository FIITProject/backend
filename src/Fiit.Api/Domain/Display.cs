using System.Globalization;

namespace Fiit.Api.Domain;

/// <summary>Время хранится в UTC, показывается в часовом поясе Екатеринбурга (Н-5), летнего времени нет.</summary>
public static class Display
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(5);

    public static DateTimeOffset Local(DateTimeOffset utc) => utc.ToOffset(Offset);

    /// <summary>«05.10 14:30».</summary>
    public static string Short(DateTimeOffset utc) => Local(utc).ToString("dd.MM HH:mm", CultureInfo.InvariantCulture);

    /// <summary>«05.10.2026 14:30».</summary>
    public static string Full(DateTimeOffset utc) => Local(utc).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string Score(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>«2 д 3 ч», «5 ч 10 мин», «12 мин».</summary>
    public static string Duration(TimeSpan d)
    {
        if (d.TotalDays >= 1)
            return d.Hours > 0 ? $"{(int)d.TotalDays} д {d.Hours} ч" : $"{(int)d.TotalDays} д";
        if (d.TotalHours >= 1)
            return d.Minutes > 0 ? $"{(int)d.TotalHours} ч {d.Minutes} мин" : $"{(int)d.TotalHours} ч";
        return $"{Math.Max(1, (int)Math.Ceiling(d.TotalMinutes))} мин";
    }
}
