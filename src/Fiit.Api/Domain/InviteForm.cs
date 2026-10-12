namespace Fiit.Api.Domain;

/// <summary>Ссылка-форма для сбора студентов в подгруппы препода по предмету.</summary>
public class InviteForm
{
    public int Id { get; set; }
    public int TeacherId { get; set; }
    public int SubjectId { get; set; }
    public required string Token { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public List<Course> Courses { get; set; } = [];
}

/// <summary>Заявка студента по форме. BindCode — одноразовый код для привязки Telegram через t.me/bot?start=code.</summary>
public class Application
{
    public int Id { get; set; }
    public int FormId { get; set; }
    public int EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? TelegramHint { get; set; }
    public DateTimeOffset PdConsentAt { get; set; }
    public required string BindCode { get; set; }
    public DateTimeOffset? BoundAt { get; set; }
}
