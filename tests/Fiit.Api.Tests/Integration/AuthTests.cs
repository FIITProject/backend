using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fiit.Contracts;

namespace Fiit.Api.Tests.Integration;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Login_then_me_returns_teacher()
    {
        var (client, id, login) = await factory.TeacherAsync("Иван Иванов");
        var me = await client.Get<MeResponse>("/api/me");
        Assert.Equal(id, me.Id);
        Assert.Equal(login, me.Login);
        Assert.False(me.MustChangePassword);
    }

    [Fact]
    public async Task Wrong_password_is_401_with_generic_message()
    {
        var response = await factory.CreateClient().Post("/api/auth/login", new LoginRequest("nobody", "x"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Неверный логин или пароль", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Anonymous_and_bot_cannot_call_teacher_api_and_teacher_cannot_call_bot_api()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Bot().GetAsync("/api/courses")).StatusCode);
        var (teacher, _, _) = await factory.TeacherAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await teacher.GetAsync("/api/bot/recruiting")).StatusCode);
        var wrongKey = factory.CreateClient();
        wrongKey.DefaultRequestHeaders.Add("X-Bot-Key", "nope");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrongKey.GetAsync("/api/bot/recruiting")).StatusCode);
    }

    [Fact]
    public async Task Health_is_anonymous() =>
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/health")).StatusCode);

    /// <summary>П-10.1, П-1.4: временный пароль → обязательная смена → обычная работа.</summary>
    [Fact]
    public async Task New_teacher_must_change_temporary_password()
    {
        var (admin, _, _) = await factory.TeacherAsync();
        var login = $"new{ApiFactory.Next()}";
        var created = await admin.Post("/api/teachers", new CreateTeacherRequest("Петров Пётр", login)).Ok<TempPasswordResponse>();

        var client = factory.CreateClient();
        var auth = await client.Post("/api/auth/login", new LoginRequest(login, created.TempPassword)).Ok<LoginResponse>();
        Assert.True(auth.MustChangePassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/courses")).StatusCode);
        Assert.True((await client.Get<MeResponse>("/api/me")).MustChangePassword);

        var changed = await client.Post("/api/auth/change-password", new ChangePasswordRequest(created.TempPassword, "new-password-1")).Ok<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", changed.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/courses")).StatusCode);
    }

    /// <summary>П-1.2, П-10.3, П-10.4: сброс пароля и отключение сразу завершают сессии.</summary>
    [Fact]
    public async Task Reset_and_disable_end_sessions()
    {
        var (admin, _, _) = await factory.TeacherAsync();
        var login = $"col{ApiFactory.Next()}";
        var created = await admin.Post("/api/teachers", new CreateTeacherRequest("Коллега", login)).Ok<TempPasswordResponse>();
        var colleague = await LoggedInAsync(login, created.TempPassword, "pass-word-123");
        Assert.Equal(HttpStatusCode.OK, (await colleague.GetAsync("/api/courses")).StatusCode);

        var (other, _, _) = await factory.TeacherAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsync($"/api/teachers/{created.TeacherId}/disable", null)).StatusCode);

        var reset = await other.PostAsync($"/api/teachers/{created.TeacherId}/reset-password", null).Ok<TempPasswordResponse>();
        Assert.Equal(HttpStatusCode.Unauthorized, (await colleague.GetAsync("/api/me")).StatusCode);

        colleague = await LoggedInAsync(login, reset.TempPassword, "pass-word-456");
        await admin.PostAsync($"/api/teachers/{created.TeacherId}/disable", null).Ok();
        Assert.Equal(HttpStatusCode.Unauthorized, (await colleague.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().Post("/api/auth/login", new LoginRequest(login, "pass-word-456"))).StatusCode);

        var list = await admin.Get<TeacherDto[]>("/api/teachers");
        Assert.True(list.Single(t => t.Id == created.TeacherId).IsDisabled);
    }

    private async Task<HttpClient> LoggedInAsync(string login, string tempPassword, string newPassword)
    {
        var client = factory.CreateClient();
        var auth = await client.Post("/api/auth/login", new LoginRequest(login, tempPassword)).Ok<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        var changed = await client.Post("/api/auth/change-password", new ChangePasswordRequest(tempPassword, newPassword)).Ok<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", changed.Token);
        return client;
    }
}
