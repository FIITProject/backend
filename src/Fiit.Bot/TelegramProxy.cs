using System.Net;

namespace Fiit.Bot;

public static class TelegramProxy
{
    /// <summary>
    /// <c>socks5://user:pass@host:port</c> → WebProxy с учётными данными. Схемы — socks5, socks4, http, https
    /// (SocketsHttpHandler понимает SOCKS сам). Логин и пароль в URL WebProxy не берёт, поэтому передаются отдельно.
    /// </summary>
    public static WebProxy Create(string url)
    {
        var uri = new Uri(url.Trim());
        if (uri.Scheme is not ("socks5" or "socks5h" or "socks4" or "socks4a" or "http" or "https"))
            throw new ArgumentException($"Telegram:Proxy: неподдерживаемая схема {uri.Scheme}");
        var proxy = new WebProxy(new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri);
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            proxy.Credentials = new NetworkCredential(Uri.UnescapeDataString(parts[0]),
                parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "");
        }
        return proxy;
    }
}
