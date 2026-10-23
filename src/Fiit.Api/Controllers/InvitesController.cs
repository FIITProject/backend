using Fiit.Api.Data;
using Fiit.Api.Domain;
using Fiit.Api.Services;
using Fiit.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fiit.Api.Controllers;

/// <summary>П-4: приглашения в бота.</summary>
[ApiController]
[Route("api/invites")]
public class InvitesController(AppDbContext db, Access access, IOptions<BotOptions> bot, TimeProvider time) : ControllerBase
{
    /// <summary>Приглашения, где есть хотя бы один курс препода.</summary>
    [HttpGet]
    public async Task<InviteDto[]> List(CancellationToken ct)
    {
        var my = access.MyCourses().Select(c => c.Id);
        var invites = await db.Invites
            .Include(i => i.Subject)
            .Include(i => i.Author)
            .Include(i => i.Courses).ThenInclude(c => c.Subgroup).ThenInclude(s => s.Group)
            .Where(i => i.Courses.Any(c => my.Contains(c.Id)))
            .OrderByDescending(i => i.CreatedAt)
            .AsSplitQuery()
            .ToListAsync(ct);
        var ids = invites.Select(i => i.Id).ToList();
        var counts = await db.Enrollments.Where(e => e.InviteId != null && ids.Contains(e.InviteId.Value))
            .GroupBy(e => e.InviteId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var now = time.GetUtcNow();
        return invites.Select(i => ToDto(i, counts.GetValueOrDefault(i.Id), now)).ToArray();
    }

    /// <summary>П-4.1: предмет и один или несколько активных курсов этого предмета в одном семестре.</summary>
    [HttpPost]
    public async Task<InviteDto> Create(CreateInviteRequest request, CancellationToken ct)
    {
        if (request.CourseIds.Length == 0)
            throw ApiException.BadRequest("Выберите хотя бы один курс");
        var courses = await access.CoursesAsync(request.CourseIds, write: true, ct);
        if (courses.Any(c => c.SubjectId != request.SubjectId))
            throw ApiException.BadRequest("Все курсы приглашения должны быть по выбранному предмету");
        if (courses.Select(c => c.Semester).Distinct().Count() > 1)
            throw ApiException.BadRequest("Все курсы приглашения должны быть из одного семестра");
        var now = time.GetUtcNow();
        if (request.ExpiresAt is { } e && e <= now)
            throw ApiException.BadRequest("Срок действия уже прошёл");

        var invite = new Invite
        {
            Code = Secrets.GenerateInviteCode(),
            AuthorId = access.Me,
            SubjectId = request.SubjectId,
            ExpiresAt = request.ExpiresAt,
            CreatedAt = now,
            Courses = courses,
        };
        db.Invites.Add(invite);
        await db.SaveChangesAsync(ct);
        await db.Entry(invite).Reference(i => i.Subject).LoadAsync(ct);
        await db.Entry(invite).Reference(i => i.Author).LoadAsync(ct);
        return ToDto(invite, 0, now);
    }

    /// <summary>П-4.4: изменить срок или закрыть досрочно.</summary>
    [HttpPatch("{id:int}")]
    public async Task<InviteDto> Update(int id, UpdateInviteRequest request, CancellationToken ct)
    {
        var my = access.MyCourses().Select(c => c.Id);
        var invite = await db.Invites
            .Include(i => i.Subject)
            .Include(i => i.Author)
            .Include(i => i.Courses).ThenInclude(c => c.Subgroup).ThenInclude(s => s.Group)
            .FirstOrDefaultAsync(i => i.Id == id && i.Courses.Any(c => my.Contains(c.Id)), ct)
            ?? throw ApiException.NotFound("Приглашение не найдено");
        var now = time.GetUtcNow();
        if (invite.ClosedAt is not null)
            throw ApiException.Conflict("Приглашение уже закрыто");

        if (request.Close)
            invite.ClosedAt = now;
        else
        {
            if (request.ExpiresAt is { } e && e <= now)
                throw ApiException.BadRequest("Срок действия уже прошёл");
            invite.ExpiresAt = request.ExpiresAt;
        }
        await db.SaveChangesAsync(ct);
        var count = await db.Enrollments.CountAsync(e => e.InviteId == id, ct);
        return ToDto(invite, count, now);
    }

    private InviteDto ToDto(Invite i, int applications, DateTimeOffset now)
    {
        var first = i.Courses.First();
        return new InviteDto(i.Id, i.Code, bot.Value.InviteLink(i.Code), i.SubjectId, i.Subject.Name, first.Semester,
            i.Courses.OrderBy(Notifier.CourseName).Select(c => new CourseRef(c.Id, Notifier.CourseName(c))).ToArray(),
            i.ExpiresAt, i.StatusAt(now), applications, i.Author.FullName, i.CreatedAt);
    }
}
