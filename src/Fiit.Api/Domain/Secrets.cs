using System.Security.Cryptography;

namespace Fiit.Api.Domain;

public static class Secrets
{
    public const int MinLength = 8;

    private const string Alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Временный пароль без похожих символов (0/O, 1/l/I).</summary>
    public static string GenerateTemporary() => RandomNumberGenerator.GetString(Alphabet, 10);

    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password);

    public static bool Verify(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);

    /// <summary>Код приглашения для t.me/bot?start=… (Telegram допускает A-Z, a-z, 0-9, _ и -).</summary>
    public static string GenerateInviteCode() => RandomNumberGenerator.GetString(Alphabet, 12);
}
