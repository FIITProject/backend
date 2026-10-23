namespace Fiit.Contracts;

public enum SemesterTerm
{
    Autumn,
    Spring,
}

/// <summary>Семестр: год + осень/весна, например «2026 осень».</summary>
public record SemesterDto(int Year, SemesterTerm Term)
{
    public override string ToString() => $"{Year} {(Term == SemesterTerm.Autumn ? "осень" : "весна")}";
}

/// <summary>Короткая ссылка на курс; Name — подгруппа, например «ФТ-201-1».</summary>
public record CourseRef(int Id, string Name);

public record TagRef(int Id, string Name);

public record IdsRequest(int[] Ids);

/// <summary>Коды ошибок в ProblemDetails (расширение <c>code</c>), чтобы бот и фронт могли реагировать без разбора текста.</summary>
public static class ErrorCodes
{
    public const string InviteClosed = "invite_closed";
    public const string NoRecruiting = "no_recruiting";
    public const string AlreadyPending = "already_pending";
    public const string AlreadyEnrolled = "already_enrolled";
    public const string ConsentRequired = "consent_required";
    public const string InvalidName = "invalid_name";
    public const string CourseArchived = "course_archived";
    public const string NotPdf = "not_pdf";
    public const string FileTooLarge = "file_too_large";
    public const string NotEnrolled = "not_enrolled";
    public const string PasswordChangeRequired = "password_change_required";
    public const string ReviewChangedByColleague = "review_changed";
}
