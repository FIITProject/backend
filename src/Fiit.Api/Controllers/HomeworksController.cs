using System.Text;
using Fiit.Api.Data;
using Fiit.Api.Domain;
using Fiit.Api.Services;
using Fiit.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Controllers;

/// <summary>П-7: домашние задания. Препод видит только ДЗ своих курсов.</summary>
[ApiController]
[Route("api/homeworks")]
public class HomeworksController(
    AppDbContext db, Access access, StudentFactsLoader factsLoader, HomeworkStats stats, Notifier notifier, TimeProvider time) : ControllerBase
{
    [HttpGet]
    public async Task<HomeworkDto[]> List(int subjectId, int semesterYear, SemesterTerm semesterTerm, CancellationToken ct)
    {
        var homeworks = await access.MyHomeworks()
            .Where(h => h.SubjectId == subjectId && h.SemesterYear == semesterYear && h.SemesterTerm == semesterTerm)
            .Include(h => h.Subject)
            .Include(h => h.Author)
            .Include(h => h.Courses).ThenInclude(c => c.Subgroup).ThenInclude(s => s.Group)
            .Include(h => h.Subtasks)
            .Include(h => h.Targets)
            .OrderBy(h => h.Deadline)
            .AsSplitQuery()
            .ToListAsync(ct);
        var counts = await stats.CountsAsync(homeworks, ct);
        var locked = await LockedAsync(homeworks.Select(h => h.Id).ToList(), ct);
        return homeworks.Select(h => ToDto(h, counts[h.Id], locked.Contains(h.Id))).ToArray();
    }

    [HttpGet("{id:int}")]
    public async Task<HomeworkDto> Get(int id, CancellationToken ct)
    {
        var hw = await access.HomeworkAsync(id, ct);
        var counts = await stats.CountsAsync([hw], ct);
        return ToDto(hw, counts[hw.Id], await stats.SubtasksLockedAsync(id, ct));
    }

    /// <summary>П-7.2: создаётся черновиком.</summary>
    [HttpPost]
    public async Task<HomeworkDto> Create(HomeworkEditRequest request, CancellationToken ct)
    {
        var courses = await ValidateCoursesAsync(request, existing: null, ct);
        var hw = new Homework
        {
            SubjectId = request.SubjectId,
            SemesterYear = request.Semester.Year,
            SemesterTerm = request.Semester.Term,
            AuthorId = access.Me,
            Title = "",
            Link = "",
            CreatedAt = time.GetUtcNow(),
            Courses = courses,
        };
        ApplyFields(hw, request);
        hw.Subtasks = ValidSubtasks(request.Subtasks).Select((s, i) => new Subtask { Title = s.Title, MaxScore = s.MaxScore, Order = i }).ToList();
        hw.Targets = await ValidTargetsAsync(request.Targets, courses, null, ct);
        db.Homeworks.Add(hw);
        await db.SaveChangesAsync(ct);
        return await Get(hw.Id, ct);
    }

    /// <summary>
    /// П-7.3, П-7.6. После публикации: название/ссылка — без уведомления; дедлайн и подзадачи — уведомление
    /// (если NotifyStudents); подзадачи — только пока нет баллов по ним; адресатов можно только добавлять.
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<HomeworkDto> Update(int id, HomeworkEditRequest request, CancellationToken ct)
    {
        var hw = await access.HomeworkAsync(id, ct);
        if (hw.Courses.All(c => c.IsArchived))
            throw ApiException.Archived();
        if (request.SubjectId != hw.SubjectId || request.Semester != new SemesterDto(hw.SemesterYear, hw.SemesterTerm))
            throw ApiException.BadRequest("Предмет и семестр ДЗ менять нельзя");

        var courses = await ValidateCoursesAsync(request, hw, ct);
        var targets = await ValidTargetsAsync(request.Targets, courses, hw, ct);
        var subtasks = ValidSubtasks(request.Subtasks);

        var oldDeadline = hw.Deadline;
        var subtasksChanged = !hw.Subtasks.OrderBy(s => s.Order).Select(s => (s.Title, s.MaxScore))
            .SequenceEqual(subtasks.Select(s => (s.Title, s.MaxScore)));
        if (subtasksChanged && await stats.SubtasksLockedAsync(id, ct))
            throw ApiException.Conflict("Подзадачи менять нельзя: по ним уже выставлены баллы");

        List<int> oldRecipients = [];
        Dictionary<int, StudentFacts> facts = [];
        if (hw.IsPublished)
        {
            facts = await factsLoader.ForCoursesAsync(hw.Courses.Select(c => c.Id).Union(courses.Select(c => c.Id)).ToList(), ct);
            oldRecipients = StudentFactsLoader.Recipients(hw, facts);
        }

        ApplyFields(hw, request);
        hw.Courses.Clear();
        hw.Courses.AddRange(courses);
        db.HomeworkTargets.RemoveRange(hw.Targets);
        hw.Targets = targets;
        if (subtasksChanged)
            ReplaceSubtasks(hw, request.Subtasks);

        if (hw.IsPublished)
        {
            var newRecipients = StudentFactsLoader.Recipients(hw, facts);
            if (oldRecipients.Except(newRecipients).Any())
                throw ApiException.Conflict("После публикации круг адресатов можно только расширить");

            var changes = new StringBuilder();
            if (hw.Deadline != oldDeadline)
                changes.AppendLine($"Дедлайн изменён: {Display.Full(oldDeadline)} → {Display.Full(hw.Deadline)}");
            if (subtasksChanged)
                changes.AppendLine("Подзадачи: " + (subtasks.Count == 0 ? "нет" : string.Join("; ", subtasks.Select(s => s.MaxScore is { } m ? $"{s.Title} ({Display.Score(m)})" : s.Title))));
            var alreadyNotified = await db.HomeworkRecipients.Where(r => r.HomeworkId == id).Select(r => r.StudentId).ToListAsync(ct);
            if (request.NotifyStudents && changes.Length > 0)
                foreach (var s in newRecipients.Intersect(alreadyNotified))
                    notifier.HomeworkChanged(s, hw, changes.ToString().TrimEnd());
            NotifyNew(hw, newRecipients.Except(alreadyNotified));
        }

        await db.SaveChangesAsync(ct);
        return await Get(id, ct);
    }

    /// <summary>П-7.4: публикация — уведомления адресатам; адресаты фиксируются (П-7.5).</summary>
    [HttpPost("{id:int}/publish")]
    public async Task<PublishResponse> Publish(int id, CancellationToken ct)
    {
        var hw = await access.HomeworkAsync(id, ct);
        if (hw.IsPublished)
            throw ApiException.Conflict("ДЗ уже опубликовано");
        if (hw.Courses.Any(c => c.IsArchived))
            throw ApiException.Archived();

        var facts = await factsLoader.ForCoursesAsync(hw.Courses.Select(c => c.Id).ToList(), ct);
        var recipients = StudentFactsLoader.Recipients(hw, facts);
        if (recipients.Count == 0)
            throw ApiException.BadRequest("У ДЗ нет ни одного адресата");

        hw.PublishedAt = time.GetUtcNow();
        NotifyNew(hw, recipients);
        await db.SaveChangesAsync(ct);
        return new PublishResponse(recipients.Count);
    }

    /// <summary>П-7.3, П-7.7: черновик удаляется всегда, опубликованное — только если по нему нет сдач.</summary>
    [HttpDelete("{id:int}")]
    public async Task Delete(int id, CancellationToken ct)
    {
        var hw = await access.HomeworkAsync(id, ct);
        if (await db.Submissions.AnyAsync(s => s.HomeworkId == id, ct))
            throw ApiException.Conflict("По ДЗ уже есть сдачи — удалить нельзя");
        await db.HomeworkRecipients.Where(r => r.HomeworkId == id).ExecuteDeleteAsync(ct);
        db.Homeworks.Remove(hw);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Итоговое число студентов для формы ДЗ.</summary>
    [HttpPost("preview-recipients")]
    public async Task<RecipientsPreviewResponse> Preview(RecipientsPreviewRequest request, CancellationToken ct)
    {
        var courses = await access.CoursesAsync(request.CourseIds, write: false, ct);
        var facts = await factsLoader.ForCoursesAsync(courses.Select(c => c.Id).ToList(), ct);
        var targets = request.Targets.Select(t => new HomeworkTarget { Kind = t.Kind, RefId = t.RefId }).ToList();
        var ids = courses.Select(c => c.Id).ToList();
        return new RecipientsPreviewResponse(facts.Values.Count(f => Recipients.Matches(ids, targets, f)));
    }

    private void NotifyNew(Homework hw, IEnumerable<int> studentIds)
    {
        var now = time.GetUtcNow();
        foreach (var s in studentIds)
        {
            db.HomeworkRecipients.Add(new HomeworkRecipient { HomeworkId = hw.Id, StudentId = s, NotifiedAt = now });
            notifier.HomeworkPublished(s, hw);
        }
    }

    private static void ApplyFields(Homework hw, HomeworkEditRequest r)
    {
        var title = r.Title.Trim();
        if (title.Length is 0 or > 300)
            throw ApiException.BadRequest("Название — от 1 до 300 символов");
        var link = r.Link.Trim();
        if (link.Length > 2000 || !Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw ApiException.BadRequest("Ссылка на условие должна быть корректным http(s)-адресом");
        hw.Title = title;
        hw.Link = link;
        hw.Deadline = r.Deadline.ToUniversalTime();
    }

    private static List<SubtaskDto> ValidSubtasks(SubtaskDto[] subtasks)
    {
        var result = subtasks.Select(s => s with { Title = s.Title.Trim() }).ToList();
        if (result.Any(s => s.Title.Length is 0 or > 300))
            throw ApiException.BadRequest("Название подзадачи — от 1 до 300 символов");
        if (result.Any(s => s.MaxScore is <= 0))
            throw ApiException.BadRequest("Максимальный балл подзадачи должен быть больше нуля");
        return result;
    }

    private void ReplaceSubtasks(Homework hw, SubtaskDto[] requested)
    {
        var keep = requested.Where(s => s.Id is not null).Select(s => s.Id!.Value).ToHashSet();
        foreach (var removed in hw.Subtasks.Where(s => !keep.Contains(s.Id)).ToList())
        {
            hw.Subtasks.Remove(removed);
            db.Subtasks.Remove(removed);
        }
        for (var i = 0; i < requested.Length; i++)
        {
            var r = requested[i];
            var subtask = r.Id is { } sid ? hw.Subtasks.FirstOrDefault(s => s.Id == sid) : null;
            if (subtask is null)
                hw.Subtasks.Add(subtask = new Subtask { Title = r.Title.Trim() });
            subtask.Title = r.Title.Trim();
            subtask.MaxScore = r.MaxScore;
            subtask.Order = i;
        }
    }

    /// <summary>Курсы ДЗ: активные курсы препода того же предмета и семестра. Чужие и архивные курсы, уже бывшие в ДЗ, можно оставить.</summary>
    private async Task<List<Course>> ValidateCoursesAsync(HomeworkEditRequest r, Homework? existing, CancellationToken ct)
    {
        if (r.CourseIds.Length == 0)
            throw ApiException.BadRequest("Выберите хотя бы один курс");
        var kept = existing?.Courses.Where(c => r.CourseIds.Contains(c.Id)).ToList() ?? [];
        var added = r.CourseIds.Except(kept.Select(c => c.Id)).ToList();
        var courses = kept.Concat(await access.CoursesAsync(added, write: true, ct)).ToList();
        if (courses.Any(c => c.SubjectId != r.SubjectId || c.SemesterYear != r.Semester.Year || c.SemesterTerm != r.Semester.Term))
            throw ApiException.BadRequest("Курсы ДЗ должны быть по одному предмету и в одном семестре");
        return courses;
    }

    private async Task<List<HomeworkTarget>> ValidTargetsAsync(HomeworkTargetDto[] targets, List<Course> courses, Homework? existing, CancellationToken ct)
    {
        var courseIds = courses.Select(c => c.Id).ToList();
        foreach (var t in targets.Distinct())
        {
            var known = existing?.Targets.Any(x => x.Kind == t.Kind && x.RefId == t.RefId) == true;
            var ok = t.Kind switch
            {
                HomeworkTargetKind.Course => courseIds.Contains(t.RefId),
                HomeworkTargetKind.Student => await db.Enrollments.AnyAsync(e => e.StudentId == t.RefId && courseIds.Contains(e.CourseId) && e.Status == EnrollmentStatus.Approved, ct),
                HomeworkTargetKind.Tag => known || await db.Tags.AnyAsync(x => x.Id == t.RefId && x.TeacherId == access.Me, ct),
                _ => false,
            };
            if (!ok)
                throw ApiException.BadRequest("Адресаты должны быть из курсов ДЗ");
        }
        return targets.Distinct().Select(t => new HomeworkTarget { Kind = t.Kind, RefId = t.RefId }).ToList();
    }

    private async Task<HashSet<int>> LockedAsync(List<int> ids, CancellationToken ct) =>
        (await db.SubtaskScores
            .Join(db.Reviews, s => s.ReviewId, r => r.Id, (s, r) => r.SubmissionId)
            .Join(db.Submissions, sid => sid, x => x.Id, (sid, x) => x.HomeworkId)
            .Where(h => ids.Contains(h))
            .Distinct()
            .ToListAsync(ct)).ToHashSet();

    private static HomeworkDto ToDto(Homework h, HomeworkCounts counts, bool subtasksLocked) => new(
        h.Id, h.SubjectId, h.Subject.Name, new SemesterDto(h.SemesterYear, h.SemesterTerm), h.Title, h.Link, h.Deadline,
        h.IsPublished, h.PublishedAt, h.Author.FullName,
        h.Subtasks.OrderBy(s => s.Order).Select(s => new SubtaskDto(s.Id, s.Title, s.MaxScore)).ToArray(),
        h.Courses.OrderBy(Notifier.CourseName).Select(c => new CourseRef(c.Id, Notifier.CourseName(c))).ToArray(),
        h.Targets.Select(t => new HomeworkTargetDto(t.Kind, t.RefId)).ToArray(),
        counts.Recipients, counts.Submitted, counts.Reviewed, subtasksLocked, h.Courses.All(c => c.IsArchived));
}
