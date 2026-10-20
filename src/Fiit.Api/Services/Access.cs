using Fiit.Api.Auth;
using Fiit.Api.Data;
using Fiit.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Services;

/// <summary>
/// Права препода (Н-4): видит только курсы, где он ведущий или имеет доступ, и всё, что к ним относится.
/// Архивный курс — только чтение.
/// </summary>
public class Access(AppDbContext db, IHttpContextAccessor http)
{
    public int Me => http.HttpContext!.User.TeacherId();

    public IQueryable<Course> MyCourses() =>
        db.Courses.Where(c => c.AdminTeacherId == Me || c.Accesses.Any(a => a.TeacherId == Me));

    public IQueryable<Homework> MyHomeworks() =>
        db.Homeworks.Where(h => h.Courses.Any(c => c.AdminTeacherId == Me || c.Accesses.Any(a => a.TeacherId == Me)));

    public async Task<Course> CourseAsync(int id, bool write, CancellationToken ct)
    {
        var course = await MyCourses()
            .Include(c => c.Subject)
            .Include(c => c.Subgroup).ThenInclude(s => s.Group)
            .FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw ApiException.NotFound("Курс не найден");
        if (write && course.IsArchived)
            throw ApiException.Archived();
        return course;
    }

    public async Task<List<Course>> CoursesAsync(IReadOnlyCollection<int> ids, bool write, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        var courses = await MyCourses()
            .Include(c => c.Subject)
            .Include(c => c.Subgroup).ThenInclude(s => s.Group)
            .Where(c => distinct.Contains(c.Id))
            .ToListAsync(ct);
        if (courses.Count != distinct.Count)
            throw ApiException.NotFound("Курс не найден");
        if (write && courses.Any(c => c.IsArchived))
            throw ApiException.Archived();
        return courses;
    }

    public async Task<Homework> HomeworkAsync(int id, CancellationToken ct) =>
        await MyHomeworks()
            .Include(h => h.Subject)
            .Include(h => h.Author)
            .Include(h => h.Courses).ThenInclude(c => c.Subgroup).ThenInclude(s => s.Group)
            .Include(h => h.Subtasks)
            .Include(h => h.Targets)
            .AsSplitQuery()
            .FirstOrDefaultAsync(h => h.Id == id, ct) ?? throw ApiException.NotFound("ДЗ не найдено");
}
