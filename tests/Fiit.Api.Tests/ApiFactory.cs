using Fiit.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Fiit.Api.Tests;

public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Db", _db.GetConnectionString());
        builder.UseSetting("Jwt:Key", "test-key-test-key-test-key-test-key-123");
        builder.UseSetting("Db:MigrateOnStartup", "true");
    }

    public async Task WithDb(Func<AppDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task InitializeAsync() => _db.StartAsync();

    Task IAsyncLifetime.DisposeAsync() => _db.DisposeAsync().AsTask();
}
