using Fiit.Api.Data;
using Fiit.Api.Domain;
using Fiit.Api.Services;
using Fiit.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Controllers;

/// <summary>П-2 (курсы), П-2.5 (архив), П-3 (доступы).</summary>
[ApiController]
[Route("api/courses")]
public class CoursesController(AppDbContext db, Access access, Notifier notifier, TimeProvider time) : ControllerBase
{
    [HttpGet]
    public async Task<CourseDto[]> List(bool archived = false, int? subjectId = null, int? semesterYear = null, SemesterTerm? semesterTerm = null, CancellationToken ct = default)
    {
        var me = access.Me;
        var query = access.MyCourses().Where(c => (c.ArchivedAt != null) == archived);
        if (subjectId is not null)
            query = query.Where(c => c.SubjectId == subjectId);
        if (semesterYear is not null && semesterTerm is not null)
            query = query.Where(c => c.SemesterYear == semesterYear && c.SemesterTerm == semesterTerm);

        var rows = await query
            .OrderByDescending(c => c.SemesterYear).ThenBy(c => c.SemesterTerm).ThenBy(c => c.Subject.Name)
            .ThenBy(c => c.Subgroup.Group.Name).ThenBy(c => c.Subgroup.Number)
            .Select(c => new
            {
                c.Id, c.SubjectId, Subject = c.Subject.Name, Group = c.Subgroup.Group.Name, c.Subgroup.Number,
                c.SemesterYear, c.SemesterTerm, c.AdminTeacherId, Admin = c.AdminTeacher.FullName, c.ArchivedAt,
                Approved = db.Enrollments.Count(e => e.CourseId == c.Id && e.Status == EnrollmentStatus.Approved),
                Pending = db.Enrollments.Count(e => e.CourseId == c.Id && e.Status == EnrollmentStatus.Pending),
            })
            .ToListAsync(ct);
        return rows.Select(c => new CourseDto(c.Id, c.SubjectId, c.Subject, c.Group, Subgroup.FullName(c.Group, c.Number),
            new SemesterDto(c.SemesterYear, c.SemesterTerm), c.AdminTeacherId, c.Admin, c.AdminTeacherId == me,
            c.ArchivedAt != null, c.Approved, c.Pending)).ToArray();
    }

    [HttpGet("{id:int}")]
    public async Task<CourseDto> Get(int id, CancellationToken ct)
    {
        var course = await access.CourseAsync(id, write: false, ct);
        return (await List(course.IsArchived, course.SubjectId, course.SemesterYear, course.SemesterTerm, ct)).Single(c => c.Id == id);
    }

    /// <summary>П-2.2: группа нормализуется, «ФТ-201-1» раскладывается на группу и подгруппу; создатель — ведущий.</summary>
    [HttpPost]
    public async Task<CourseDto> Create(CreateCourseRequest request, CancellationToken ct)
    {
        var subject = await db.Subjects.FindAsync([request.SubjectId], ct) ?? throw ApiException.NotFound("Предмет не найден");
        var parsed = GroupName.Parse(request.Group, request.Subgroup)
            ?? throw ApiException.BadRequest("Не удалось разобрать группу. Пример: ФТ-201 и подгруппа 1, или ФТ-201-1");
        var semester = request.Semester ?? Semesters.At(time.GetUtcNow());
        if (semester.Year is < 2000 or > 2100)
            throw ApiException.BadRequest("Некорректный год семестра");

        var group = await db.Groups.FirstOrDefaultAsync(g => g.Name == parsed.Group, ct) ?? db.Groups.Add(new Group { Name = parsed.Group }).Entity;
        var subgroup = group.Id == 0
            ? null
            : await db.Subgroups.FirstOrDefaultAsync(s => s.GroupId == group.Id && s.Number == parsed.Subgroup, ct);
        subgroup ??= db.Subgroups.Add(new Subgroup { Group = group, Number = parsed.Subgroup }).Entity;

        if (subgroup.Id != 0)
        {
            var existing = await db.Courses.Include(c => c.AdminTeacher).FirstOrDefaultAsync(c =>
                c.SubjectId == subject.Id && c.SubgroupId == subgroup.Id && c.SemesterYear == semester.Year && c.SemesterTerm == semester.Term, ct);
            if (existing is not null)
                throw ApiException.Conflict(
                    $"У {Subgroup.FullName(parsed.Group, parsed.Subgroup)} по {subject.Name} в семестре {semester} уже есть преподаватель ({existing.AdminTeacher.FullName}). Попросите у него доступ");
        }

        var course = new Course
        {
            Subject = subject,
            Subgroup = subgroup,
            SemesterYear = semester.Year,
            SemesterTerm = semester.Term,
            AdminTeacherId = access.Me,
            CreatedAt = time.GetUtcNow(),
        };
        db.Courses.Add(course);
        await db.SaveChangesAsync(ct);
        return await Get(course.Id, ct);
    }

    /// <summary>П-2.4: только ведущий и только пустой курс (без заявок и ДЗ).</summary>
    [HttpDelete("{id:int}")]
    public async Task Delete(int id, CancellationToken ct)
    {
        var course = await access.CourseAsync(id, write: false, ct);
        if (course.AdminTeacherId != access.Me)
            throw ApiException.Forbidden("Удалить курс может только ведущий преподаватель");
        if (await db.Enrollments.AnyAsync(e => e.CourseId == id, ct) || await db.Homeworks.AnyAsync(h => h.Courses.Any(c => c.Id == id), ct))
            throw ApiException.Conflict("В курсе есть студенты, заявки или ДЗ — удалить нельзя. Отправьте его в архив");
        db.Courses.Remove(course);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// П-2.5: архивировать может любой преподаватель курса. Приглашения, где остались только архивные курсы, закрываются;
    /// ожидающие заявки отклоняются с причиной «Набор закрыт».
    /// </summary>
    [HttpPost("archive")]
    public async Task Archive(CourseIdsRequest request, CancellationToken ct)
    {
        var courses = await access.CoursesAsync(request.CourseIds, write: false, ct);
        var now = time.GetUtcNow();
        var ids = courses.Select(c => c.Id).ToList();
        foreach (var c in courses)
            c.ArchivedAt ??= now;

        var pending = await db.Enrollments
            .Include(e => e.Course).ThenInclude(c => c.Subject)
            .Include(e => e.Course).ThenInclude(c => c.Subgroup).ThenInclude(s => s.Group)
            .Include(e => e.Course).ThenInclude(c => c.AdminTeacher)
            .Where(e => ids.Contains(e.CourseId) && e.Status == EnrollmentStatus.Pending)
            .ToListAsync(ct);
        foreach (var e in pending)
        {
            e.Status = EnrollmentStatus.Rejected;
            e.RejectReason = "Набор закрыт";
            e.DecidedAt = now;
            e.DecidedById = access.Me;
            notifier.ApplicationRejected(e.StudentId, e.Course, e.RejectReason);
        }

        var invites = await db.Invites.Include(i => i.Courses)
            .Where(i => i.ClosedAt == null && i.Courses.Any(c => ids.Contains(c.Id)))
            .ToListAsync(ct);
        foreach (var invite in invites.Where(i => i.Courses.All(c => c.ArchivedAt != null)))
            invite.ClosedAt = now;

        await db.SaveChangesAsync(ct);
    }

    [HttpPost("unarchive")]
    public async Task Unarchive(CourseIdsRequest request, CancellationToken ct)
    {
        foreach (var c in await access.CoursesAsync(request.CourseIds, write: false, ct))
            c.ArchivedAt = null;
        await db.SaveChangesAsync(ct);
    }

    [HttpGet("{id:int}/access")]
    public async Task<CourseAccessDto[]> AccessList(int id, CancellationToken ct)
    {
        await access.CourseAsync(id, write: false, ct);
        return await db.CourseAccesses.Where(a => a.CourseId == id)
            .OrderBy(a => a.Teacher.FullName)
            .Select(a => new CourseAccessDto(a.TeacherId, a.Teacher.FullName, a.Teacher.Login))
            .ToArrayAsync(ct);
    }

    /// <summary>П-3.1: выдаёт и отзывает доступ только ведущий преподаватель курса.</summary>
    [HttpPost("{id:int}/access/{teacherId:int}")]
    public async Task Grant(int id, int teacherId, CancellationToken ct)
    {
        var course = await RequireAdmin(id, ct);
        if (teacherId == course.AdminTeacherId)
            throw ApiException.BadRequest("Ведущий преподаватель и так имеет полный доступ");
        var teacher = await db.Teachers.FindAsync([teacherId], ct) ?? throw ApiException.NotFound("Преподаватель не найден");
        if (teacher.DisabledAt is not null)
            throw ApiException.BadRequest("Преподаватель отключён");
        if (!await db.CourseAccesses.AnyAsync(a => a.CourseId == id && a.TeacherId == teacherId, ct))
        {
            db.CourseAccesses.Add(new CourseAccess { CourseId = id, TeacherId = teacherId });
            await db.SaveChangesAsync(ct);
        }
    }

    [HttpDelete("{id:int}/access/{teacherId:int}")]
    public async Task Revoke(int id, int teacherId, CancellationToken ct)
    {
        await RequireAdmin(id, ct);
        await db.CourseAccesses.Where(a => a.CourseId == id && a.TeacherId == teacherId).ExecuteDeleteAsync(ct);
    }

    private async Task<Course> RequireAdmin(int id, CancellationToken ct)
    {
        var course = await access.CourseAsync(id, write: true, ct);
        if (course.AdminTeacherId != access.Me)
            throw ApiException.Forbidden("Управлять доступами может только ведущий преподаватель курса");
        return course;
    }
}
