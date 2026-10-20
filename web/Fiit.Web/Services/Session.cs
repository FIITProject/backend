using Fiit.Contracts;
using Microsoft.JSInterop;

namespace Fiit.Web.Services;

/// <summary>Текущий преподаватель и его токен (хранится в localStorage браузера).</summary>
public class Session(IJSRuntime js)
{
    private const string Key = "fiit.token";

    public string? Token { get; private set; }
    public MeResponse? Me { get; set; }
    public bool IsLoggedIn => Token is not null;

    public event Action? Changed;

    public async Task LoadAsync() => Token = await js.InvokeAsync<string?>("localStorage.getItem", Key);

    public async Task SetAsync(string token)
    {
        Token = token;
        await js.InvokeVoidAsync("localStorage.setItem", Key, token);
        Changed?.Invoke();
    }

    public async Task ClearAsync()
    {
        Token = null;
        Me = null;
        await js.InvokeVoidAsync("localStorage.removeItem", Key);
        Changed?.Invoke();
    }

    public void Notify() => Changed?.Invoke();
}
