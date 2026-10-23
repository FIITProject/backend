using Fiit.Contracts;

namespace Fiit.Api.Domain;

/// <summary>Студент. Определяется по Telegram-аккаунту; ФИО — из последней анкеты.</summary>
public class Student
{
    public int Id { get; set; }
    public long TelegramUserId { get; set; }
    public string? TelegramUsername { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    /// <summary>Нажатие «Согласен» в боте (Н-9).</summary>
    public DateTimeOffset PdConsentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Бот не смог доставить сообщение — студент заблокировал бота (С-3.2).</summary>
    public DateTimeOffset? BotBlockedAt { get; set; }

    public string FullName => $"{LastName} {FirstName}";
}

/// <summary>
/// Заявка / студент в курсе. Pending → Approved | Rejected; Approved → Removed.
/// После отклонения студент заполняет анкету заново — новая запись. Активная (Pending/Approved) — не больше одной на курс.
/// </summary>
public class Enrollment
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public int? InviteId { get; set; }
    public EnrollmentStatus Status { get; set; }
    public string? RejectReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public int? DecidedById { get; set; }
}

/// <summary>Личная метка препода.</summary>
public class Tag
{
    public int Id { get; set; }
    public int TeacherId { get; set; }
    public required string Name { get; set; }
}

public class StudentTag
{
    public int TagId { get; set; }
    public int StudentId { get; set; }
}
