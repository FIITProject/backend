using Fiit.Api.Data;
using Fiit.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api;

/// <summary>
/// Служебные команды того же образа:
/// <c>migrate</c> — применить миграции БД (отдельный шаг деплоя, до запуска новой версии API);
/// <c>add-teacher &lt;login&gt; &lt;password&gt; &lt;full name&gt;</c> — первый преподаватель заводится так (П-10).
/// </summary>
public static class Cli
{
    public static bool IsCommand(string[] args) => args is ["migrate", ..] or ["add-teacher", ..];

    public static Task<int> RunAsync(WebApplication app, string[] args) => args[0] switch
    {
        "migrate" => MigrateAsync(app),
        _ => AddTeacherAsync(app, args),
    };

    private static async Task<int> MigrateAsync(WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            Console.WriteLine("Database is up to date");
            return 0;
        }
        Console.WriteLine($"Applying migrations: {string.Join(", ", pending)}");
        await db.Database.MigrateAsync();
        Console.WriteLine("Done");
        return 0;
    }

    private static async Task<int> AddTeacherAsync(WebApplication app, string[] args)
    {
        if (args is not ["add-teacher", var login, var password, .. var nameParts] || nameParts.Length == 0)
        {
            Console.Error.WriteLine("Usage: add-teacher <login> <password> <full name>");
            return 1;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if ((await db.Database.GetPendingMigrationsAsync()).Any())
        {
            Console.Error.WriteLine("Database schema is not up to date, run 'migrate' first");
            return 1;
        }

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
