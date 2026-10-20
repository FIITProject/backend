using ClosedXML.Excel;
using Fiit.Api.Data;
using Fiit.Api.Domain;
using Fiit.Api.Services;
using Fiit.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Controllers;

/// <summary>П-8: сводная таблица «студенты своих курсов × опубликованные ДЗ своих курсов».</summary>
[ApiController]
[Route("api/gradebook")]
public class GradebookController(AppDbContext db, Access access, StudentFactsLoader factsLoader, HomeworkStats stats, TimeProvider time) : ControllerBase
{
    [HttpGet]
    public Task<GradebookDto> Get(int subjectId, int semesterYear, SemesterTerm semesterTerm, [FromQuery] int[]? courseIds, string? group, int? tagId, CancellationToken ct) =>
        BuildAsync(subjectId, semesterYear, semesterTerm, courseIds, group, tagId, ct);

    /// <summary>П-8.7: экспорт текущего вида с учётом фильтров.</summary>
    [HttpGet("export.xlsx")]
    public async Task<FileContentResult> Export(int subjectId, int semesterYear, SemesterTerm semesterTerm, [FromQuery] int[]? courseIds, string? group, int? tagId, CancellationToken ct)
    {
        var book = await BuildAsync(subjectId, semesterYear, semesterTerm, courseIds, group, tagId, ct);
        var subject = await db.Subjects.FindAsync([subjectId], ct);

        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Оценки");
        string[] fixedHeaders = ["Фамилия", "Имя", "Группа", "Подгруппа"];
        for (var i = 0; i < fixedHeaders.Length; i++)
            ws.Cell(1, i + 1).Value = fixedHeaders[i];
        var col = fixedHeaders.Length + 1;
        foreach (var hw in book.Homeworks)
        {
            ws.Cell(1, col).Value = hw.Title;
            ws.Cell(1, col + 1).Value = "Опоздание";
            col += 2;
        }
        ws.Cell(1, col).Value = "Среднее";

        var row = 2;
        foreach (var r in book.Rows)
        {
            ws.Cell(row, 1).Value = r.LastName;
            ws.Cell(row, 2).Value = r.FirstName;
            ws.Cell(row, 3).Value = r.GroupName;
            ws.Cell(row, 4).Value = r.SubgroupName;
            col = fixedHeaders.Length + 1;
            foreach (var cell in r.Cells)
            {
                if (cell.Score is { } score && cell.Status is CellStatus.Reviewed or CellStatus.ReviewedWithUnsentChanges)
                    ws.Cell(row, col).Value = score;
                else
                    ws.Cell(row, col).Value = StatusText(cell.Status);
                if (cell.IsLate)
                    ws.Cell(row, col + 1).Value = "да";
                col += 2;
            }
            if (r.Average is { } avg)
                ws.Cell(row, col).Value = avg;
            row++;
        }
        ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Min(row, 200));

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        var name = $"{subject?.Name ?? "Оценки"} {new SemesterDto(semesterYear, semesterTerm)}.xlsx";
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    public static string StatusText(CellStatus status) => status switch
    {
        CellStatus.NotAssigned => "не назначено",
        CellStatus.NotSubmitted => "не сдано",
        CellStatus.Overdue => "не сдано, просрочено",
        CellStatus.OnReview => "на проверке",
        CellStatus.Reviewed => "проверено",
        CellStatus.ReviewedWithUnsentChanges => "проверено, есть неотправленные правки",
        _ => "",
    };

    private async Task<GradebookDto> BuildAsync(int subjectId, int year, SemesterTerm term, int[]? courseIds, string? group, int? tagId, CancellationToken ct)
    {
        var coursesQuery = access.MyCourses().Where(c => c.SubjectId == subjectId && c.SemesterYear == year && c.SemesterTerm == term);
        if (courseIds is { Length: > 0 })
            coursesQuery = coursesQuery.Where(c => courseIds.Contains(c.Id));
        if (!string.IsNullOrWhiteSpace(group))
        {
            var g = GroupName.Parse(group, null)?.Group ?? group.Trim().ToUpperInvariant();
            coursesQuery = coursesQuery.Where(c => c.Subgroup.Group.Name == g);
        }
        var courses = await coursesQuery.Include(c => c.Subgroup).ThenInclude(s => s.Group).ToListAsync(ct);
        var rowCourseIds = courses.Select(c => c.Id).ToList();

        var enrollmentsQuery = db.Enrollments.Where(e => rowCourseIds.Contains(e.CourseId) && e.Status == EnrollmentStatus.Approved);
        if (tagId is not null)
            enrollmentsQuery = enrollmentsQuery.Where(e => db.StudentTags.Any(t => t.TagId == tagId && t.StudentId == e.StudentId));
        var students = (await enrollmentsQuery.Include(e => e.Student).ToListAsync(ct))
            .GroupBy(e => e.StudentId).Select(g => g.First())
            .OrderBy(e => e.Student.LastName).ThenBy(e => e.Student.FirstName)
            .ToList();

        var homeworks = await access.MyHomeworks()
            .Where(h => h.SubjectId == subjectId && h.SemesterYear == year && h.SemesterTerm == term && h.PublishedAt != null)
            .Include(h => h.Courses).Include(h => h.Targets)
            .OrderBy(h => h.Deadline).ThenBy(h => h.Id)
            .AsSplitQuery()
            .ToListAsync(ct);
        var hwIds = homeworks.Select(h => h.Id).ToList();
        var counts = await stats.CountsAsync(homeworks, ct);
        var latest = await stats.LatestWithCountAsync(hwIds, ct);
        var facts = await factsLoader.ForCoursesAsync(homeworks.SelectMany(h => h.Courses).Select(c => c.Id).Union(rowCourseIds).Distinct().ToList(), ct);
        var now = time.GetUtcNow();
        var courseById = courses.ToDictionary(c => c.Id);

        var rows = students.Select(e =>
        {
            var f = facts.GetValueOrDefault(e.StudentId) ?? new StudentFacts(e.StudentId, new HashSet<int>(), new HashSet<int>());
            var cells = homeworks.Select(h =>
            {
                var assigned = Recipients.Matches(h.Courses.Select(c => c.Id).ToList(), h.Targets, f);
                var hasSub = latest.TryGetValue((h.Id, e.StudentId), out var info);
                var status = Grading.Cell(assigned, hasSub ? info.Latest : null, h.Deadline, now);
                var sentScore = hasSub && info.Latest.Review is { IsSent: true } r ? r.SentTotal : null;
                var late = hasSub && Grading.IsLate(info.Latest.SubmittedAt, h.Deadline);
                return new GradebookCell(h.Id, status, sentScore, late, hasSub ? info.Versions : 0, hasSub ? info.Latest.Id : null);
            }).ToArray();
            var average = Grading.Average(cells.Where(c => c.Status is CellStatus.Reviewed or CellStatus.ReviewedWithUnsentChanges).Select(c => c.Score));
            var course = courseById[e.CourseId];
            return new GradebookRow(e.StudentId, e.Student.LastName, e.Student.FirstName, course.Subgroup.Group.Name,
                Notifier.CourseName(course), course.Id, cells, average);
        }).ToArray();

        var columns = homeworks.Select(h => new GradebookHomework(h.Id, h.Title, h.Deadline, h.Link,
            counts[h.Id].Recipients, counts[h.Id].Submitted, counts[h.Id].Reviewed)).ToArray();
        return new GradebookDto(columns, rows);
    }
}
