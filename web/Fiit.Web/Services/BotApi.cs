using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fiit.Contracts;

namespace Fiit.Web.Services;

/// <summary>Клиент /api/bot/* с сервисным ключом — то же, что будет делать настоящий бот.</summary>
public class BotApi(HttpClient http, IConfiguration config)
{
    public string Key { get; set; } = config["BotKey"] ?? "";

    public Task<BotInviteDto> Invite(string code) => Get<BotInviteDto>($"api/bot/invites/{Uri.EscapeDataString(code)}");
    public Task<BotRecruitingDto[]> Recruiting() => Get<BotRecruitingDto[]>("api/bot/recruiting");
    public Task<BotStudentDto> Student(long tgId) => Get<BotStudentDto>($"api/bot/students/{tgId}");
    public Task<BotApplicationResponse> Apply(BotApplicationRequest r) => Send<BotApplicationResponse>(HttpMethod.Post, "api/bot/applications", JsonContent.Create(r, options: Api.Json));
    public Task<BotHomeworksDto> Homeworks(long tgId) => Get<BotHomeworksDto>($"api/bot/students/{tgId}/homeworks");
    public Task<BotHomeworkDto> Homework(long tgId, int id) => Get<BotHomeworkDto>($"api/bot/students/{tgId}/homeworks/{id}");
    public Task<BotNotificationDto[]> Claim() => Send<BotNotificationDto[]>(HttpMethod.Post, "api/bot/notifications/claim", JsonContent.Create(new ClaimRequest(50), options: Api.Json));
    public Task Ack(int id) => Send<object?>(HttpMethod.Post, $"api/bot/notifications/{id}/ack", null, read: false);

    public Task<BotSubmitResponse> Submit(long tgId, int homeworkId, byte[] pdf, string fileName)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", fileName);
        return Send<BotSubmitResponse>(HttpMethod.Post, $"api/bot/students/{tgId}/homeworks/{homeworkId}/submissions", content);
    }

    public async Task<byte[]> NotificationFile(int id)
    {
        using var response = await Raw(HttpMethod.Get, $"api/bot/notifications/{id}/file", null);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private Task<T> Get<T>(string url) => Send<T>(HttpMethod.Get, url, null);

    private async Task<T> Send<T>(HttpMethod method, string url, HttpContent? content, bool read = true)
    {
        using var response = await Raw(method, url, content);
        return read ? (await response.Content.ReadFromJsonAsync<T>(Api.Json))! : default!;
    }

    private async Task<HttpResponseMessage> Raw(HttpMethod method, string url, HttpContent? content)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Add("X-Bot-Key", Key);
        var response = await http.SendAsync(request);
        if (response.IsSuccessStatusCode)
            return response;
        var (message, code) = await Api.ReadProblem(response);
        throw new ApiError(response.StatusCode, message, code);
    }
}
