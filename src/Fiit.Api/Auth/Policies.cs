namespace Fiit.Api.Auth;

public static class Policies
{
    /// <summary>Препод с действующим токеном, даже если ещё не сменил временный пароль (смена пароля, /api/me).</summary>
    public const string AnyTeacher = "any-teacher";

    /// <summary>Сервисный ключ бота (заголовок X-Bot-Key).</summary>
    public const string Bot = "bot";

    public const string MustChangePasswordClaim = "must_change_password";
}
