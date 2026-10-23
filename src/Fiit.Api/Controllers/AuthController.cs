using Fiit.Api.Auth;
using Fiit.Api.Data;
using Fiit.Api.Domain;
using Fiit.Api.Services;
using Fiit.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, TokenService tokens, TimeProvider time) : ControllerBase
{
    /// <summary>П-1.1: при ошибке — «Неверный логин или пароль», без уточнения.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<LoginResponse> Login(LoginRequest request, CancellationToken ct)
    {
        var teacher = await db.Teachers.SingleOrDefaultAsync(t => t.Login == request.Login.Trim(), ct);
        if (teacher is null || !Secrets.Verify(request.Password, teacher.PasswordHash))
            throw new ApiException(StatusCodes.Status401Unauthorized, "Неверный логин или пароль");
        if (teacher.DisabledAt is not null)
            throw new ApiException(StatusCodes.Status401Unauthorized, "Учётная запись отключена");

        return new LoginResponse(tokens.Issue(teacher), teacher.MustChangePassword);
    }

    /// <summary>П-1.4: старый + новый пароль. Завершает остальные сессии и возвращает новый токен.</summary>
    [Authorize(Policy = Policies.AnyTeacher)]
    [HttpPost("change-password")]
    public async Task<LoginResponse> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var teacher = await db.Teachers.SingleAsync(t => t.Id == User.TeacherId(), ct);
        if (!Secrets.Verify(request.OldPassword, teacher.PasswordHash))
            throw ApiException.BadRequest("Старый пароль неверен");
        if (request.NewPassword.Length < Secrets.MinLength)
            throw ApiException.BadRequest($"Новый пароль — не короче {Secrets.MinLength} символов");
        if (request.NewPassword == request.OldPassword)
            throw ApiException.BadRequest("Новый пароль совпадает со старым");

        teacher.PasswordHash = Secrets.Hash(request.NewPassword);
        teacher.MustChangePassword = false;
        teacher.SessionsValidFrom = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return new LoginResponse(tokens.Issue(teacher), false);
    }
}
