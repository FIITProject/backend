using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fiit.Contracts;

namespace Fiit.Bot.Core;

/// <summary>Ошибка API: Message — человеческий текст из ProblemDetails, его можно показать студенту как есть.</summary>
public class BotApiException(HttpStatusCode status, string message, string? code) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    public string? Code { get; } = code;
}

/// <summary>Всё, что боту нужно от бэкенда (/api/bot/*, заголовок X-Bot-Key).</summary>
public interface IBotApi
{
    Task<BotInviteDto> InviteAsync(string code, CancellationToken ct);
    Task<BotRecruitingDto[]> RecruitingAsync(CancellationToken ct);
    Task<BotStudentDto> StudentAsync(long telegramUserId, CancellationToken ct);
    Task<BotApplicationResponse> ApplyAsync(BotApplicationRequest request, CancellationToken ct);
    Task<BotHomeworksDto> HomeworksAsync(long telegramUserId, CancellationToken ct);
    Task<BotHomeworkDto> HomeworkAsync(long telegramUserId, int homeworkId, CancellationToken ct);
    Task<BotSubmitResponse> SubmitAsync(long telegramUserId, int homeworkId, byte[] pdf, string fileName, CancellationToken ct);
    Task<BotNotificationStateDto> NotificationStateAsync(int id, CancellationToken ct);
    Task NotificationDeliveredAsync(int id, CancellationToken ct);
    Task NotificationFailedAsync(int id, BotDeliveryFailedRequest request, CancellationToken ct);
    Task<byte[]> NotificationFileAsync(int id, CancellationToken ct);
    Task<BotNotificationDto[]> StudentNotificationsAsync(long telegramUserId, int afterId, CancellationToken ct);
}

public class BotApiClient(HttpClient http, Func<string> botKey) : IBotApi
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public Task<BotInviteDto> InviteAsync(string code, CancellationToken ct) => GetAsync<BotInviteDto>($"api/bot/invites/{Uri.EscapeDataString(code)}", ct);
    public Task<BotRecruitingDto[]> RecruitingAsync(CancellationToken ct) => GetAsync<BotRecruitingDto[]>("api/bot/recruiting", ct);
    public Task<BotStudentDto> StudentAsync(long tgId, CancellationToken ct) => GetAsync<BotStudentDto>($"api/bot/students/{tgId}", ct);
    public Task<BotApplicationResponse> ApplyAsync(BotApplicationRequest r, CancellationToken ct) => SendAsync<BotApplicationResponse>(HttpMethod.Post, "api/bot/applications", JsonContent.Create(r, options: Json), ct);
    public Task<BotHomeworksDto> HomeworksAsync(long tgId, CancellationToken ct) => GetAsync<BotHomeworksDto>($"api/bot/students/{tgId}/homeworks", ct);
    public Task<BotHomeworkDto> HomeworkAsync(long tgId, int id, CancellationToken ct) => GetAsync<BotHomeworkDto>($"api/bot/students/{tgId}/homeworks/{id}", ct);
    public Task<BotNotificationStateDto> NotificationStateAsync(int id, CancellationToken ct) => GetAsync<BotNotificationStateDto>($"api/bot/notifications/{id}", ct);
    public Task<BotNotificationDto[]> StudentNotificationsAsync(long tgId, int afterId, CancellationToken ct) => GetAsync<BotNotificationDto[]>($"api/bot/students/{tgId}/notifications?afterId={afterId}", ct);

    public async Task NotificationDeliveredAsync(int id, CancellationToken ct) =>
        (await RawAsync(HttpMethod.Post, $"api/bot/notifications/{id}/delivered", null, ct)).Dispose();

    public async Task NotificationFailedAsync(int id, BotDeliveryFailedRequest request, CancellationToken ct) =>
        (await RawAsync(HttpMethod.Post, $"api/bot/notifications/{id}/failed", JsonContent.Create(request, options: Json), ct)).Dispose();

    public async Task<byte[]> NotificationFileAsync(int id, CancellationToken ct)
    {
        using var response = await RawAsync(HttpMethod.Get, $"api/bot/notifications/{id}/file", null, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public Task<BotSubmitResponse> SubmitAsync(long tgId, int homeworkId, byte[] pdf, string fileName, CancellationToken ct)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", fileName);
        return SendAsync<BotSubmitResponse>(HttpMethod.Post, $"api/bot/students/{tgId}/homeworks/{homeworkId}/submissions", content, ct);
    }

    private Task<T> GetAsync<T>(string url, CancellationToken ct) => SendAsync<T>(HttpMethod.Get, url, null, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string url, HttpContent? content, CancellationToken ct)
    {
        using var response = await RawAsync(method, url, content, ct);
        return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    private async Task<HttpResponseMessage> RawAsync(HttpMethod method, string url, HttpContent? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Add("X-Bot-Key", botKey());
        var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
            return response;
        using (response)
        {
            var (message, code) = await ReadProblemAsync(response, ct);
            throw new BotApiException(response.StatusCode, message, code);
        }
    }

    private static async Task<(string Message, string? Code)> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var doc = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var title = doc.TryGetProperty("title", out var t) ? t.GetString() : null;
            var code = doc.TryGetProperty("code", out var c) ? c.GetString() : null;
            return (title ?? $"Ошибка {(int)response.StatusCode}", code);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            return ($"Ошибка {(int)response.StatusCode}", null);
        }
    }
}
