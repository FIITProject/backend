using Fiit.Api.Data;
using Fiit.Api.Domain;
using Fiit.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Services;

/// <summary>Загружает <see cref="StudentFacts"/> (подтверждённые курсы и метки) для расчёта адресатов ДЗ.</summary>
public class StudentFactsLoader(AppDbContext db)
{
    public async Task<Dictionary<int, StudentFacts>> ForCoursesAsync(IReadOnlyCollection<int> courseIds, CancellationToken ct)
    {
        var enrollments = await db.Enrollments
            .Where(e => courseIds.Contains(e.CourseId) && e.Status == EnrollmentStatus.Approved)
            .Select(e => new { e.StudentId, e.CourseId })
            .ToListAsync(ct);
        return await BuildAsync(enrollments.Select(e => (e.StudentId, e.CourseId)), ct);
    }

    public async Task<StudentFacts> ForStudentAsync(int studentId, CancellationToken ct)
    {
        var courses = await db.Enrollments
            .Where(e => e.StudentId == studentId && e.Status == EnrollmentStatus.Approved)
            .Select(e => e.CourseId)
            .ToListAsync(ct);
        var tags = await db.StudentTags.Where(t => t.StudentId == studentId).Select(t => t.TagId).ToListAsync(ct);
        return new StudentFacts(studentId, courses.ToHashSet(), tags.ToHashSet());
    }

    private async Task<Dictionary<int, StudentFacts>> BuildAsync(IEnumerable<(int StudentId, int CourseId)> enrollments, CancellationToken ct)
    {
        var byStudent = enrollments.GroupBy(e => e.StudentId).ToDictionary(g => g.Key, g => g.Select(e => e.CourseId).ToHashSet());
        var ids = byStudent.Keys.ToList();
        var tags = (await db.StudentTags.Where(t => ids.Contains(t.StudentId)).ToListAsync(ct))
            .GroupBy(t => t.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(t => t.TagId).ToHashSet());
        return byStudent.ToDictionary(
            kv => kv.Key,
            kv => new StudentFacts(kv.Key, kv.Value, tags.GetValueOrDefault(kv.Key) ?? []));
    }

    public static List<int> Recipients(Homework hw, IReadOnlyDictionary<int, StudentFacts> facts)
    {
        var courseIds = hw.Courses.Select(c => c.Id).ToList();
        return facts.Values.Where(f => Domain.Recipients.Matches(courseIds, hw.Targets, f)).Select(f => f.StudentId).ToList();
    }
}
