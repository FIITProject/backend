using Fiit.Api.Auth;
using Fiit.Api.Data;
using Fiit.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Controllers;

[ApiController]
[Route("api/me")]
[Authorize(Policy = Policies.AnyTeacher)]
public class MeController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<MeResponse> Get(CancellationToken ct)
    {
        var t = await db.Teachers.SingleAsync(t => t.Id == User.TeacherId(), ct);
        return new MeResponse(t.Id, t.Login, t.FullName, t.MustChangePassword);
    }
}
