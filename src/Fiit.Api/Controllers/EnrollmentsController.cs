using Fiit.Api.Data;
using Fiit.Api.Domain;
using Fiit.Api.Services;
using Fiit.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Controllers;

/// <summary>П-5: студенты и модерация заявок.</summary>
[ApiController]
[Route("api/enrollments")]
public class EnrollmentsController(AppDbContext db, Access access, Notifier notifier, TimeProvider time) : ControllerBase
{
    /// <summary>Список студентов курсов препода. Отклонённые и удалённые по умолчанию скрыты.</summary>
    [HttpGet]
    public async Task<EnrollmentDto[]> List(
        [FromQuery] int[]? courseIds, EnrollmentStatus? status, int? tagId, string? q, bool includeHistory = false, CancellationToken ct = default)
    {
        var my = access.MyCourses().Select(c => c.Id);
        var query = db.Enrollments.Where(e => my.Contains(e.CourseId));
        if (courseIds is { Length: > 0 })
            query = query.Where(e => courseIds.Contains(e.CourseId));
        if (status is not null)
            query = query.Where(e => e.Status == status);
        else if (!includeHistory)
            query = query.Where(e => e.Status == EnrollmentStatus.Pending || e.Status == EnrollmentStatus.Approved);
        if (tagId is not null)
            query = query.Where(e => db.StudentTags.Any(t => t.TagId == tagId && t.StudentId == e.StudentId));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(e => EF.Functions.ILike(e.Student.LastName, pattern));
        }

        var rows = await query
            .OrderBy(e => e.Student.LastName).ThenBy(e => e.Student.FirstName)
            .Select(e => new
            {
                e.Id, e.StudentId, e.Student.LastName, e.Student.FirstName, e.CourseId, Subject = e.Course.Subject.Name,
                Group = e.Course.Subgroup.Group.Name, e.Course.Subgroup.Number, e.Student.TelegramUsername, e.Status,
                e.CreatedAt, e.RejectReason, Blocked = e.Student.BotBlockedAt != null,
            })
            .ToListAsync(ct);

        var me = access.Me;
        var studentIds = rows.Select(r => r.StudentId).Distinct().ToList();
        var tags = (await db.StudentTags
                .Where(st => studentIds.Contains(st.StudentId))
                .Join(db.Tags.Where(t => t.TeacherId == me), st => st.TagId, t => t.Id, (st, t) => new { st.StudentId, t.Id, t.Name })
                .ToListAsync(ct))
            .GroupBy(x => x.StudentId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Name).Select(x => new TagRef(x.Id, x.Name)).ToArray());

        return rows.Select(r => new EnrollmentDto(r.Id, r.StudentId, r.LastName, r.FirstName, r.CourseId, r.Subject,
            Subgroup.FullName(r.Group, r.Number), r.TelegramUsername, r.Status, r.CreatedAt, r.RejectReason, r.Blocked,
            tags.GetValueOrDefault(r.StudentId) ?? [])).ToArray();
    }

    [HttpPost("approve")]
    public async Task<int> Approve(IdsRequest request, CancellationToken ct)
    {
        var pending = await LoadPending(request.Ids, ct);
        var now = time.GetUtcNow();
        foreach (var e in pending)
        {
            e.Status = EnrollmentStatus.Approved;
            e.DecidedAt = now;
            e.DecidedById = access.Me;
            notifier.ApplicationApproved(e.StudentId, e.Course);
        }
        await db.SaveChangesAsync(ct);
        return pending.Count;
    }

    /// <summary>П-5.4: отклонить с необязательной причиной, которую увидит студент.</summary>
    [HttpPost("reject")]
    public async Task<int> Reject(RejectRequest request, CancellationToken ct)
    {
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason?.Length > 500)
            throw ApiException.BadRequest("Причина — до 500 символов");
        var pending = await LoadPending(request.Ids, ct);
        var now = time.GetUtcNow();
        foreach (var e in pending)
        {
            e.Status = EnrollmentStatus.Rejected;
            e.RejectReason = reason;
            e.DecidedAt = now;
            e.DecidedById = access.Me;
            notifier.ApplicationRejected(e.StudentId, e.Course, reason);
        }
        await db.SaveChangesAsync(ct);
        return pending.Count;
    }

    /// <summary>П-5.5, П-5.6: удалить подтверждённого студента. Без уведомления; сдачи и проверки сохраняются.</summary>
    [HttpDelete("{id:int}")]
    public async Task Remove(int id, CancellationToken ct)
    {
        var e = await db.Enrollments.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw ApiException.NotFound("Студент не найден");
        await access.CourseAsync(e.CourseId, write: true, ct);
        if (e.Status != EnrollmentStatus.Approved)
            throw ApiException.Conflict("Удалить можно только подтверждённого студента; заявку — отклонить");
        e.Status = EnrollmentStatus.Removed;
        e.DecidedAt = time.GetUtcNow();
        e.DecidedById = access.Me;
        await db.SaveChangesAsync(ct);
    }

    private async Task<List<Enrollment>> LoadPending(int[] ids, CancellationToken ct)
    {
        var enrollments = await db.Enrollments
            .Include(e => e.Course).ThenInclude(c => c.Subject)
            .Include(e => e.Course).ThenInclude(c => c.Subgroup).ThenInclude(s => s.Group)
            .Include(e => e.Course).ThenInclude(c => c.AdminTeacher)
            .Where(e => ids.Contains(e.Id))
            .ToListAsync(ct);
        if (enrollments.Count != ids.Distinct().Count())
            throw ApiException.NotFound("Заявка не найдена");
        await access.CoursesAsync(enrollments.Select(e => e.CourseId).ToList(), write: true, ct);
        return enrollments.Where(e => e.Status == EnrollmentStatus.Pending).ToList();
    }
}
