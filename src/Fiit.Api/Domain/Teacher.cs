namespace Fiit.Api.Domain;

public class Teacher
{
    public int Id { get; set; }
    public required string Login { get; set; }
    public required string PasswordHash { get; set; }
    public required string FullName { get; set; }
    /// <summary>Вошёл с временным паролем — до смены пароля доступны только /api/auth и /api/me.</summary>
    public bool MustChangePassword { get; set; }
    public int? AddedById { get; set; }
    public Teacher? AddedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
    /// <summary>Токены, выпущенные раньше этого момента, недействительны (отключение, смена/сброс пароля).</summary>
    public DateTimeOffset SessionsValidFrom { get; set; }
}
