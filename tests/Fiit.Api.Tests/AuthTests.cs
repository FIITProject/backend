using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fiit.Api.Domain;

namespace Fiit.Api.Tests;

public class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private record TokenDto(string Token);

    private record MeDto(int Id, string Login, string FullName);

    [Fact]
    public async Task Login_then_me_returns_teacher()
    {
        var client = factory.CreateClient();
        await factory.WithDb(async db =>
        {
            db.Teachers.Add(new Teacher { Login = "ivanov", PasswordHash = BCrypt.Net.BCrypt.HashPassword("secret"), FullName = "Иван Иванов" });
            await db.SaveChangesAsync();
        });

        var login = await client.PostAsJsonAsync("/api/auth/login", new { login = "ivanov", password = "secret" });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<TokenDto>())!.Token;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.GetFromJsonAsync<MeDto>("/api/me");

        Assert.Equal("ivanov", me!.Login);
        Assert.Equal("Иван Иванов", me.FullName);
    }

    [Fact]
    public async Task Login_with_wrong_password_is_401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { login = "nobody", password = "x" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoint_without_token_is_401()
    {
        var response = await factory.CreateClient().GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_is_anonymous()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
