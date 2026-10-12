using Fiit.Api.Auth;
using Fiit.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace Fiit.Api.Controllers;

[ApiController]
[Route("api/me")]
public class MeController(AppDbContext db) : ControllerBase
{
    public record MeResponse(int Id, string Login, string FullName);

    [HttpGet]
    public async Task<ActionResult<MeResponse>> Get(CancellationToken ct)
    {
        var teacher = await db.Teachers.FindAsync([User.TeacherId()], ct);
        return teacher is null ? Unauthorized() : new MeResponse(teacher.Id, teacher.Login, teacher.FullName);
    }
}
