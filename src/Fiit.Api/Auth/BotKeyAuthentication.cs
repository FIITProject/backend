using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Fiit.Api.Auth;

public class BotKeyOptions : AuthenticationSchemeOptions
{
    public string Key { get; set; } = "";
}

/// <summary>Бот ходит в /api/bot/* с сервисным ключом в заголовке X-Bot-Key.</summary>
public class BotKeyAuthentication(IOptionsMonitor<BotKeyOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<BotKeyOptions>(options, logger, encoder)
{
    public const string SchemeName = "BotKey";
    public const string Header = "X-Bot-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var provided))
            return Task.FromResult(AuthenticateResult.NoResult());

        var expected = Encoding.UTF8.GetBytes(Options.Key);
        if (expected.Length == 0 || !CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(provided.ToString())))
            return Task.FromResult(AuthenticateResult.Fail("Invalid bot key"));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "bot")], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
