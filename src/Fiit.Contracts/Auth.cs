namespace Fiit.Contracts;

public record LoginRequest(string Login, string Password);

public record LoginResponse(string Token, bool MustChangePassword);

public record ChangePasswordRequest(string OldPassword, string NewPassword);

public record MeResponse(int Id, string Login, string FullName, bool MustChangePassword);

public record TeacherDto(
    int Id,
    string FullName,
    string Login,
    int? AddedById,
    string? AddedByName,
    DateTimeOffset CreatedAt,
    bool IsDisabled,
    bool CanDisable);

public record CreateTeacherRequest(string FullName, string Login);

/// <summary>Временный пароль показывается один раз — добавивший сам передаёт его коллеге.</summary>
public record TempPasswordResponse(int TeacherId, string Login, string TempPassword);
