using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fiit.Api.Data;
using Fiit.Api.Domain;
using Fiit.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;

namespace Fiit.Api.Tests;

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

/// <summary>API на настоящих Postgres и S3 (Testcontainers). Одна база на все интеграционные тесты — данные у тестов уникальные.</summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string BotKey = "test-bot-key";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17").Build();
    private const string S3AccessKey = "fiit-test";
    private const string S3SecretKey = "fiit-test-secret";

    // S3-совместимое хранилище вместо Yandex Object Storage (образы MinIO из Docker Hub больше не раздаются)
    private readonly IContainer _s3 = new ContainerBuilder("rustfs/rustfs:1.0.3")
        .WithEnvironment("RUSTFS_ACCESS_KEY", S3AccessKey)
        .WithEnvironment("RUSTFS_SECRET_KEY", S3SecretKey)
        .WithCommand("/data")
        .WithPortBinding(9000, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9000).ForPath("/health")))
        .Build();
    private static int _seq;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Db", _db.GetConnectionString());
        builder.UseSetting("Jwt:Key", "test-key-test-key-test-key-test-key-123");
        builder.UseSetting("Db:MigrateOnStartup", "true");
        builder.UseSetting("Bot:Key", BotKey);
        builder.UseSetting("Bot:Username", "test_bot");
        builder.UseSetting("Storage:ServiceUrl", $"http://{_s3.Hostname}:{_s3.GetMappedPublicPort(9000)}");
        builder.UseSetting("Storage:AccessKey", S3AccessKey);
        builder.UseSetting("Storage:SecretKey", S3SecretKey);
        builder.UseSetting("Storage:Bucket", "test");
        builder.UseSetting("Storage:CreateBucket", "true");
    }

    public static int Next() => Interlocked.Increment(ref _seq);

    public async Task WithDb(Func<AppDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task<T> WithDb<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Препод в БД (как после add-teacher) и клиент с его токеном.</summary>
    public async Task<(HttpClient Client, int Id, string Login)> TeacherAsync(string? name = null, string password = "secret-password")
    {
        var login = $"t{Next()}";
        var id = await WithDb(async db =>
        {
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            var t = new Teacher { Login = login, PasswordHash = Secrets.Hash(password), FullName = name ?? $"Преподаватель {login}", CreatedAt = now, SessionsValidFrom = now };
            db.Teachers.Add(t);
            await db.SaveChangesAsync();
            return t.Id;
        });
        var client = CreateClient();
        var login1 = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(login, password));
        login1.EnsureSuccessStatusCode();
        var token = (await login1.Content.ReadFromJsonAsync<LoginResponse>(Json))!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, id, login);
    }

    public HttpClient Bot()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Bot-Key", BotKey);
        return client;
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_db.StartAsync(), _s3.StartAsync());
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _db.DisposeAsync();
        await _s3.DisposeAsync();
    }
}

public static class HttpExtensions
{
    public static async Task<T> Ok<T>(this Task<HttpResponseMessage> request)
    {
        var response = await request;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json))!;
    }

    public static async Task Ok(this Task<HttpResponseMessage> request)
    {
        var response = await request;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    public static Task<HttpResponseMessage> Post<T>(this HttpClient c, string url, T body) => c.PostAsJsonAsync(url, body, ApiFactory.Json);

    public static Task<HttpResponseMessage> Put<T>(this HttpClient c, string url, T body) => c.PutAsJsonAsync(url, body, ApiFactory.Json);

    public static async Task<T> Get<T>(this HttpClient c, string url) => await c.GetAsync(url).Ok<T>();

    public static async Task<string?> ErrorCode(this HttpResponseMessage response)
    {
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        return doc.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
