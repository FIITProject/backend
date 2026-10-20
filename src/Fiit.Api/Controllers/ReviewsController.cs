using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fiit.Api.Data;
using Fiit.Api.Domain;
using Fiit.Api.Services;
using Fiit.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Controllers;

/// <summary>П-9: проверка работы — версии, черновик проверки с автосохранением, отправка студенту.</summary>
[ApiController]
public class ReviewsController(AppDbContext db, Access access, IFileStorage storage, Notifier notifier, TimeProvider time) : ControllerBase
{
    [HttpGet("api/homeworks/{homeworkId:int}/students/{studentId:int}/review")]
    public async Task<ReviewContextDto> Context(int homeworkId, int studentId, CancellationToken ct)
    {
        var hw = await access.HomeworkAsync(homeworkId, ct);
        var (student, course) = await StudentInHomeworkAsync(hw, studentId, ct);
        var submissions = await db.Submissions
            .Include(s => s.Review).ThenInclude(r => r!.SubtaskScores)
            .Include(s => s.Review).ThenInclude(r => r!.UpdatedBy)
            .Include(s => s.Review).ThenInclude(r => r!.SentBy)
            .Where(s => s.HomeworkId == homeworkId && s.StudentId == studentId)
            .OrderBy(s => s.Version)
            .AsSplitQuery()
            .ToListAsync(ct);
        var latestVersion = submissions.LastOrDefault()?.Version;

        return new ReviewContextDto(hw.Id, hw.Title, hw.Link, hw.Deadline,
            hw.Subtasks.OrderBy(s => s.Order).Select(s => new SubtaskDto(s.Id, s.Title, s.MaxScore)).ToArray(),
            student.Id, student.FullName, Notifier.CourseName(course), course.IsArchived,
            submissions.Select(s => new SubmissionDto(s.Id, s.Version, s.SubmittedAt, Grading.IsLate(s.SubmittedAt, hw.Deadline),
                Grading.IsLate(s.SubmittedAt, hw.Deadline) ? s.SubmittedAt - hw.Deadline : null,
                s.FileName, s.FileSize, s.Version == latestVersion, s.Review is null ? null : ToDto(s.Review, hw))).ToArray());
    }

    /// <summary>П-9.9: оригинальный PDF студента.</summary>
    [HttpGet("api/submissions/{id:int}/file")]
    public async Task<FileContentResult> File(int id, CancellationToken ct)
    {
        var (submission, _, _) = await SubmissionAsync(id, ct);
        return File(await storage.GetAsync(submission.FileKey, ct), "application/pdf", submission.FileName);
    }

    /// <summary>PDF с впечатанными пометками, который ушёл студенту.</summary>
    [HttpGet("api/submissions/{id:int}/reviewed-file")]
    public async Task<FileContentResult> ReviewedFile(int id, CancellationToken ct)
    {
        var (submission, _, _) = await SubmissionAsync(id, ct);
        var key = submission.Review?.SentFileKey ?? throw ApiException.NotFound("Проверка ещё не отправлена");
        return File(await storage.GetAsync(key, ct), "application/pdf", $"проверка-v{submission.Version}.pdf");
    }

    /// <summary>П-9.5: черновик проверки. Править можно только последнюю версию (старые — только просмотр).</summary>
    [HttpPut("api/submissions/{id:int}/review")]
    public async Task<ReviewDto> Save(int id, SaveReviewRequest request, CancellationToken ct)
    {
        var (submission, hw, _) = await SubmissionAsync(id, ct, write: true);
        await RequireLatestAsync(submission, ct);
        if (PdfAnnotator.Parse(request.Annotations) is null)
            throw ApiException.BadRequest("Некорректные пометки");
        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        if (comment?.Length > 10_000)
            throw ApiException.BadRequest("Комментарий — до 10 000 символов");
        if (request.TotalOverride is < 0)
            throw ApiException.BadRequest("Итог не может быть отрицательным");

        var subtasks = hw.Subtasks.ToDictionary(s => s.Id);
        var scores = new List<SubtaskScore>();
        foreach (var s in request.SubtaskScores.Where(s => s.Score is not null))
        {
            if (!subtasks.TryGetValue(s.SubtaskId, out var subtask))
                throw ApiException.BadRequest("Подзадача не найдена");
            if (s.Score < 0 || subtask.MaxScore is { } max && s.Score > max)
                throw ApiException.BadRequest($"Балл за «{subtask.Title}» — от 0 до {(subtask.MaxScore is { } m ? Display.Score(m) : "∞")}");
            scores.Add(new SubtaskScore { SubtaskId = s.SubtaskId, Score = s.Score!.Value });
        }

        var review = submission.Review;
        // Проверять могут разные преподаватели; не даём молча затереть черновик, который параллельно правит коллега
        if (review is not null && request.BaseUpdatedAt is { } baseline && review.UpdatedAt != baseline && review.UpdatedById != access.Me)
            throw ApiException.Conflict($"Эту проверку только что изменил(а) {review.UpdatedBy.FullName} — обновите страницу", ErrorCodes.ReviewChangedByColleague);
        if (review is null)
        {
            review = new Review { SubmissionId = submission.Id, UpdatedById = access.Me };
            db.Reviews.Add(review);
            submission.Review = review;
        }

        // Обновляем баллы на месте: удалить и добавить тот же ключ в одной транзакции EF не даёт
        foreach (var existing in review.SubtaskScores.ToList())
        {
            var updated = scores.FirstOrDefault(s => s.SubtaskId == existing.SubtaskId);
            if (updated is null)
                review.SubtaskScores.Remove(existing);
            else
                existing.Score = updated.Score;
        }
        review.SubtaskScores.AddRange(scores.Where(s => review.SubtaskScores.All(x => x.SubtaskId != s.SubtaskId)));

        review.Annotations = request.Annotations;
        review.Comment = comment;
        review.TotalOverride = request.TotalOverride;
        review.UpdatedById = access.Me;
        review.UpdatedAt = TruncateToMicroseconds(time.GetUtcNow());
        review.DraftHash = Hash(review);
        await db.SaveChangesAsync(ct);
        await db.Entry(review).Reference(r => r.UpdatedBy).LoadAsync(ct);
        return ToDto(review, hw);
    }

    /// <summary>П-9.6, П-9.7: впечатать пометки в PDF и отправить студенту оценку, комментарий и файл. Повторная отправка — «Проверка обновлена».</summary>
    [HttpPost("api/submissions/{id:int}/review/send")]
    public async Task<ReviewDto> Send(int id, CancellationToken ct)
    {
        var (submission, hw, _) = await SubmissionAsync(id, ct, write: true);
        await RequireLatestAsync(submission, ct);
        var review = submission.Review ?? throw ApiException.BadRequest("Сначала сохраните проверку");

        byte[] annotated;
        try
        {
            var original = await storage.GetAsync(submission.FileKey, ct);
            annotated = PdfAnnotator.Apply(original, PdfAnnotator.Parse(review.Annotations) ?? []);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw ApiException.Conflict("Не удалось впечатать пометки в этот PDF (файл повреждён или защищён)");
        }

        var key = $"reviews/{submission.Id}/{Guid.NewGuid():N}.pdf";
        await storage.PutAsync(key, annotated, "application/pdf", ct);

        var updated = review.IsSent;
        var subtaskResults = hw.Subtasks.OrderBy(s => s.Order)
            .Select(s => new BotSubtaskResult(s.Title, review.SubtaskScores.FirstOrDefault(x => x.SubtaskId == s.Id)?.Score, s.MaxScore))
            .ToArray();
        review.SentAt = time.GetUtcNow();
        review.SentById = access.Me;
        review.SentBy = await db.Teachers.SingleAsync(t => t.Id == access.Me, ct);
        review.SentHash = review.DraftHash;
        review.SentTotal = Grading.Total(review.TotalOverride, review.SubtaskScores.Select(s => s.Score));
        review.SentComment = review.Comment;
        review.SentSubtasks = JsonSerializer.Serialize(subtaskResults, JsonSerializerOptions.Web);
        review.SentFileKey = key;
        notifier.ReviewSent(submission.StudentId, hw, submission, review, subtaskResults, updated);
        await db.SaveChangesAsync(ct);
        return ToDto(review, hw);
    }

    private async Task RequireLatestAsync(Submission submission, CancellationToken ct)
    {
        var latest = await db.Submissions.Where(s => s.HomeworkId == submission.HomeworkId && s.StudentId == submission.StudentId).MaxAsync(s => s.Version, ct);
        if (submission.Version != latest)
            throw ApiException.Conflict("Это старая версия — она доступна только для просмотра");
    }

    private async Task<(Submission, Homework, Course)> SubmissionAsync(int id, CancellationToken ct, bool write = false)
    {
        var submission = await db.Submissions
            .Include(s => s.Review).ThenInclude(r => r!.SubtaskScores)
            .Include(s => s.Review).ThenInclude(r => r!.UpdatedBy)
            .Include(s => s.Review).ThenInclude(r => r!.SentBy)
            .FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw ApiException.NotFound("Сдача не найдена");
        var hw = await access.HomeworkAsync(submission.HomeworkId, ct);
        var (_, course) = await StudentInHomeworkAsync(hw, submission.StudentId, ct);
        if (write && course.IsArchived)
            throw ApiException.Archived();
        return (submission, hw, course);
    }

    /// <summary>Студент должен быть (или быть когда-то) в одном из курсов ДЗ, к которым у препода есть доступ.</summary>
    private async Task<(Student, Course)> StudentInHomeworkAsync(Homework hw, int studentId, CancellationToken ct)
    {
        var hwCourseIds = hw.Courses.Select(c => c.Id).ToList();
        var my = access.MyCourses().Select(c => c.Id);
        var enrollment = await db.Enrollments
            .Include(e => e.Student)
            .Include(e => e.Course).ThenInclude(c => c.Subgroup).ThenInclude(s => s.Group)
            .Where(e => e.StudentId == studentId && hwCourseIds.Contains(e.CourseId) && my.Contains(e.CourseId))
            .OrderBy(e => e.Status == EnrollmentStatus.Approved ? 0 : 1).ThenByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(ct) ?? throw ApiException.NotFound("Студент не найден");
        return (enrollment.Student, enrollment.Course);
    }

    private static string Hash(Review r)
    {
        var payload = JsonSerializer.Serialize(new
        {
            r.Annotations,
            r.Comment,
            r.TotalOverride,
            Scores = r.SubtaskScores.OrderBy(s => s.SubtaskId).Select(s => new { s.SubtaskId, s.Score }),
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static ReviewDto ToDto(Review r, Homework hw) => new(
        r.Annotations, r.Comment,
        r.SubtaskScores.Select(s => new SubtaskScoreDto(s.SubtaskId, s.Score)).ToArray(),
        r.TotalOverride,
        Grading.Total(r.TotalOverride, r.SubtaskScores.Where(s => hw.Subtasks.Any(x => x.Id == s.SubtaskId)).Select(s => s.Score)),
        r.UpdatedAt, r.SentAt, r.HasUnsentChanges, r.UpdatedBy?.FullName ?? "", r.SentBy?.FullName);

    /// <summary>Postgres хранит время с точностью до микросекунд — иначе BaseUpdatedAt не совпадёт после чтения из БД.</summary>
    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset t) => new(t.Ticks - t.Ticks % 10, t.Offset);
}
