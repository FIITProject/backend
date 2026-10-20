using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;

namespace Fiit.Web.Services;

/// <summary>Ошибка API: Title — текст для человека из ProblemDetails, Code — машинный код.</summary>
public class ApiError(HttpStatusCode status, string message, string? code) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    public string? Code { get; } = code;
}

/// <summary>Клиент API преподавателя. Токен — в localStorage; 401 → на страницу входа.</summary>
public class Api(HttpClient http, Session session, NavigationManager nav)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public Task<T> Get<T>(string url) => Send<T>(HttpMethod.Get, url, null);
    public Task<T> Post<T>(string url, object? body = null) => Send<T>(HttpMethod.Post, url, body);
    public Task<T> Put<T>(string url, object body) => Send<T>(HttpMethod.Put, url, body);
    public Task<T> Patch<T>(string url, object body) => Send<T>(HttpMethod.Patch, url, body);
    public Task Post(string url, object? body = null) => Send<object?>(HttpMethod.Post, url, body, readBody: false);
    public Task Delete(string url) => Send<object?>(HttpMethod.Delete, url, null, readBody: false);

    public async Task<byte[]> Bytes(string url)
    {
        using var response = await SendRaw(HttpMethod.Get, url, null);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<T> Send<T>(HttpMethod method, string url, object? body, bool readBody = true)
    {
        using var response = await SendRaw(method, url, body);
        if (!readBody || response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            return default!;
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<HttpResponseMessage> SendRaw(HttpMethod method, string url, object? body)
    {
        var request = new HttpRequestMessage(method, url);
        if (session.Token is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);

        var response = await http.SendAsync(request);
        if (response.IsSuccessStatusCode)
            return response;

        var (message, code) = await ReadProblem(response);
        response.Dispose();
        if (response.StatusCode == HttpStatusCode.Unauthorized && !url.Contains("auth/"))
        {
            await session.ClearAsync();
            nav.NavigateTo("login");
        }
        throw new ApiError(response.StatusCode, message, code);
    }

    public static async Task<(string Message, string? Code)> ReadProblem(HttpResponseMessage response)
    {
        try
        {
            var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
            var title = doc.TryGetProperty("title", out var t) ? t.GetString() : null;
            var code = doc.TryGetProperty("code", out var c) ? c.GetString() : null;
            return (title ?? $"Ошибка {(int)response.StatusCode}", code);
        }
        catch (JsonException)
        {
            return ($"Ошибка {(int)response.StatusCode}", null);
        }
    }
}
