using System.Security.Claims;

namespace Fiit.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static int TeacherId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("No teacher id claim"));
}
