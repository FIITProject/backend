using System.Security.Claims;
using Fiit.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Auth;

/// <summary>
/// JWT живёт 2 недели (П-1.2), поэтому на каждом запросе проверяем, что препод не отключён и токен выпущен
/// не раньше <c>SessionsValidFrom</c> — отключение и смена/сброс пароля сразу завершают сессии.
/// </summary>
public static class SessionValidation
{
    public static async Task OnTokenValidated(TokenValidatedContext context)
    {
        var principal = context.Principal!;
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var teacherId)
            || !long.TryParse(principal.FindFirstValue(TokenService.IssuedAtMsClaim), out var issuedAt))
        {
            context.Fail("Invalid token");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var teacher = await db.Teachers.AsNoTracking()
            .Where(t => t.Id == teacherId)
            .Select(t => new { t.DisabledAt, t.SessionsValidFrom, t.MustChangePassword })
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (teacher is null || teacher.DisabledAt is not null || issuedAt < teacher.SessionsValidFrom.ToUnixTimeMilliseconds())
        {
            context.Fail("Session is no longer valid");
            return;
        }

        if (teacher.MustChangePassword)
            ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(Policies.MustChangePasswordClaim, "true"));
    }
}
