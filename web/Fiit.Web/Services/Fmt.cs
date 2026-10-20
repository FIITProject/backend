using System.Globalization;
using Fiit.Contracts;

namespace Fiit.Web.Services;

/// <summary>Время показываем в часовом поясе Екатеринбурга (UTC+5), как в ТЗ (Н-5).</summary>
public static class Fmt
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(5);

    public static string Dt(DateTimeOffset? value) => value is { } v ? v.ToOffset(Offset).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) : "—";

    public static string Score(decimal? value) => value is { } v ? v.ToString("0.##", CultureInfo.InvariantCulture) : "";

    /// <summary>Значение для input type=datetime-local.</summary>
    public static string ToInput(DateTimeOffset value) => value.ToOffset(Offset).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    public static DateTimeOffset? FromInput(string? value) =>
        DateTime.TryParseExact(value, ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? new DateTimeOffset(dt, Offset).ToUniversalTime()
            : null;

    public static string Semester(SemesterDto s) => s.ToString();

    public static string Duration(TimeSpan d) =>
        d.TotalDays >= 1 ? $"{(int)d.TotalDays} д {d.Hours} ч" : d.TotalHours >= 1 ? $"{(int)d.TotalHours} ч {d.Minutes} мин" : $"{Math.Max(1, (int)d.TotalMinutes)} мин";

    public static string Status(CellStatus s) => s switch
    {
        CellStatus.NotAssigned => "не назначено",
        CellStatus.NotSubmitted => "не сдано",
        CellStatus.Overdue => "просрочено",
        CellStatus.OnReview => "на проверке",
        CellStatus.Reviewed => "проверено",
        CellStatus.ReviewedWithUnsentChanges => "есть неотправленные правки",
        _ => "",
    };

    public static string Status(EnrollmentStatus s) => s switch
    {
        EnrollmentStatus.Pending => "ожидает подтверждения",
        EnrollmentStatus.Approved => "подтверждён",
        EnrollmentStatus.Rejected => "отклонена",
        EnrollmentStatus.Removed => "удалён",
        _ => "",
    };

    public static string Status(InviteStatus s) => s switch
    {
        InviteStatus.Active => "активно",
        InviteStatus.Expired => "истекло",
        InviteStatus.Closed => "закрыто",
        _ => "",
    };

    public static string Status(BotHomeworkStatus s) => s switch
    {
        BotHomeworkStatus.NotSubmitted => "не сдано",
        BotHomeworkStatus.Overdue => "просрочено",
        BotHomeworkStatus.OnReview => "на проверке",
        BotHomeworkStatus.Reviewed => "проверено",
        _ => "",
    };

    public static IEnumerable<SemesterDto> SemesterOptions(SemesterDto current) =>
    [
        current.Term == SemesterTerm.Autumn ? new SemesterDto(current.Year + 1, SemesterTerm.Spring) : new SemesterDto(current.Year, SemesterTerm.Autumn),
        current,
        current.Term == SemesterTerm.Autumn ? new SemesterDto(current.Year, SemesterTerm.Spring) : new SemesterDto(current.Year - 1, SemesterTerm.Autumn),
        current.Term == SemesterTerm.Autumn ? new SemesterDto(current.Year - 1, SemesterTerm.Autumn) : new SemesterDto(current.Year - 1, SemesterTerm.Spring),
    ];

    public static string Key(SemesterDto s) => $"{s.Year}-{s.Term}";

    public static SemesterDto? ParseKey(string? key) =>
        key?.Split('-') is [var y, var t] && int.TryParse(y, out var year) && Enum.TryParse<SemesterTerm>(t, out var term) ? new SemesterDto(year, term) : null;
}
