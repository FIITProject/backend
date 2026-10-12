using Fiit.Api.Auth;
using Fiit.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, TokenService tokens) : ControllerBase
{
    public record LoginRequest(string Login, string Password);

    public record LoginResponse(string Token);

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var teacher = await db.Teachers.SingleOrDefaultAsync(t => t.Login == request.Login, ct);
        if (teacher is null || !BCrypt.Net.BCrypt.Verify(request.Password, teacher.PasswordHash))
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Неверный логин или пароль");

        return new LoginResponse(tokens.Issue(teacher));
    }
}
