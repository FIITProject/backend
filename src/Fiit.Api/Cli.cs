using Fiit.Api.Data;
using Fiit.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api;

/// <summary>Служебные команды: <c>dotnet Fiit.Api.dll add-teacher &lt;login&gt; &lt;password&gt; &lt;full name&gt;</c> — первый преподаватель заводится так (П-10).</summary>
public static class Cli
{
    public static bool IsCommand(string[] args) => args is ["add-teacher", ..];

    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        if (args is not ["add-teacher", var login, var password, .. var nameParts] || nameParts.Length == 0)
        {
            Console.Error.WriteLine("Usage: add-teacher <login> <password> <full name>");
            return 1;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        if (await db.Teachers.AnyAsync(t => t.Login == login))
        {
            Console.Error.WriteLine($"Teacher '{login}' already exists");
            return 1;
        }

        var now = DateTimeOffset.UtcNow;
        db.Teachers.Add(new Teacher
        {
            Login = login,
            PasswordHash = Secrets.Hash(password),
            FullName = string.Join(' ', nameParts),
            CreatedAt = now,
            SessionsValidFrom = now,
        });
        await db.SaveChangesAsync();
        Console.WriteLine($"Teacher '{login}' created");
        return 0;
    }
}
