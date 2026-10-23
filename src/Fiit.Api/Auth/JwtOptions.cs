namespace Fiit.Api.Auth;

public class JwtOptions
{
    public const string Section = "Jwt";

    public required string Key { get; init; }
    public string Issuer { get; init; } = "fiit";
    public string Audience { get; init; } = "fiit-web";
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromDays(14);
}
