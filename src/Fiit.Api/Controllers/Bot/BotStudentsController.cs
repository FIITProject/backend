using System.Text.Json;
using Fiit.Api.Auth;
using Fiit.Api.Data;
using Fiit.Api.Domain;
using Fiit.Api.Services;
using Fiit.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Controllers.Bot;

/// <summary>
/// API для бота: анкета (С-1), «Мои задания» (С-4), сдача (С-5). Состояние диалога держит бот,
/// бэкенд отвечает на вопросы «что можно этому студенту» и принимает готовые анкеты и PDF.
/// </summary>
[ApiController]
[Route("api/bot")]
[Authorize(Policy = Policies.Bot)]
public class BotStudentsController(AppDbContext db, StudentFactsLoader factsLoader, IFileStorage storage, TimeProvider time) : ControllerBase
{
    /// <summary>С-1.1: приглашение по коду из ссылки. Закрытое/истёкшее — IsOpen = false (С-1.3).</summary>
    [HttpGet("invites/{code}")]
    public async Task<BotInviteDto> Invite(string code, CancellationToken ct)
    {
        var invite = await db.Invites
            .Include(i => i.Subject)
            .Include(i => i.Author)
            .Include(i => i.Courses).ThenInclude(c => c.Subgroup).ThenInclude(s => s.Group)
            .FirstOrDefaultAsync(i => i.Code == code, ct) ?? throw ApiException.NotFound("Приглашение не найдено");
        var active = invite.Courses.Where(c => !c.IsArchived).OrderBy(Notifier.CourseName).ToList();
        var open = invite.StatusAt(time.GetUtcNow()) == InviteStatus.Active && active.Count > 0;
        return new BotInviteDto(invite.Code, open, invite.SubjectId, invite.Subject.Name, invite.Author.FullName,
            invite.Courses.First().Semester, active.Select(c => new BotCourseOption(c.Id, Notifier.CourseName(c))).ToArray());
    }

    /// <summary>С-1.1а: студент запустил бота без ссылки — предметы, по которым сейчас идёт набор.</summary>
    [HttpGet("recruiting")]
    public async Task<BotRecruitingDto[]> Recruiting(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var invites = await db.Invites
            .Include(i => i.Subject)
            .Include(i => i.Author)
            .Include(i => i.Courses).ThenInclude(c => c.Subgroup).ThenInclude(s => s.Group)
            .Where(i => i.ClosedAt == null && (i.ExpiresAt == null || i.ExpiresAt > now))
            .AsSplitQuery()
            .ToListAsync(ct);
        return invites
            .GroupBy(i => (i.SubjectId, i.AuthorId))
            .Select(g => new BotRecruitingDto(g.Key.SubjectId, g.First().Subject.Name, g.Key.AuthorId, g.First().Author.FullName,
                g.SelectMany(i => i.Courses).Where(c => !c.IsArchived).DistinctBy(c => c.Id).OrderBy(Notifier.CourseName)
                    .Select(c => new BotCourseOption(c.Id, Notifier.CourseName(c))).ToArray()))
            .Where(r => r.Courses.Length > 0)
            .OrderBy(r => r.SubjectName).ThenBy(r => r.TeacherName)
            .ToArray();
    }

    /// <summary>Что бот знает о студенте: для С-1.2 (закрыт ли функционал), С-1.9 (повторная регистрация), С-1.10.</summary>
    [HttpGet("students/{telegramUserId:long}")]
    public async Task<BotStudentDto> Student(long telegramUserId, CancellationToken ct)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.TelegramUserId == telegramUserId, ct);
        if (student is null)
            return new BotStudentDto(false, null, null, false, false, []);

        var enrollments = await db.Enrollments
            .Where(e => e.StudentId == student.Id)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new
            {
                e.CourseId, Subject = e.Course.Subject.Name, Group = e.Course.Subgroup.Group.Name, e.Course.Subgroup.Number,
                Teacher = e.Course.AdminTeacher.FullName, e.Status, e.RejectReason,
            })
            .ToListAsync(ct);
        // По каждому курсу — последняя заявка
        var latest = enrollments.DistinctBy(e => e.CourseId)
            .Select(e => new BotEnrollmentDto(e.CourseId, e.Subject, Subgroup.FullName(e.Group, e.Number), e.Teacher, e.Status, e.RejectReason))
            .ToArray();
        return new BotStudentDto(true, student.FirstName, student.LastName, true, latest.Any(e => e.Status == EnrollmentStatus.Approved), latest);
    }

    /// <summary>С-1.4–С-1.10: принять заполненную анкету.</summary>
    [HttpPost("applications")]
    public async Task<BotApplicationResponse> Apply(BotApplicationRequest request, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var firstName = PersonName.Normalize(request.FirstName);
        var lastName = PersonName.Normalize(request.LastName);
        if (firstName is null || lastName is null)
            throw ApiException.BadRequest("Фамилия и имя — только буквы, пробел и дефис, до 100 символов", ErrorCodes.InvalidName);

        var course = await db.Courses
            .Include(c => c.Subject)
            .Include(c => c.Subgroup).ThenInclude(s => s.Group)
            .FirstOrDefaultAsync(c => c.Id == request.CourseId, ct) ?? throw ApiException.NotFound("Курс не найден");

        var openInvites = db.Invites.Where(i => i.ClosedAt == null && (i.ExpiresAt == null || i.ExpiresAt > now) && i.Courses.Any(c => c.Id == course.Id));
        var invite = request.InviteCode is null
            ? await openInvites.OrderByDescending(i => i.CreatedAt).FirstOrDefaultAsync(ct)
            : await openInvites.FirstOrDefaultAsync(i => i.Code == request.InviteCode, ct);
        if (invite is null || course.IsArchived)
            throw ApiException.Conflict("Набор закрыт, обратитесь к преподавателю", ErrorCodes.InviteClosed);

        var student = await db.Students.FirstOrDefaultAsync(s => s.TelegramUserId == request.TelegramUserId, ct);
        if (student is null)
        {
            if (!request.PdConsent)
                throw ApiException.BadRequest("Без согласия на обработку персональных данных регистрация невозможна", ErrorCodes.ConsentRequired);
            student = db.Students.Add(new Student
            {
                TelegramUserId = request.TelegramUserId,
                FirstName = firstName,
                LastName = lastName,
                PdConsentAt = now,
                CreatedAt = now,
            }).Entity;
        }
        else
        {
            var active = await db.Enrollments
                .Where(e => e.StudentId == student.Id && e.CourseId == course.Id && (e.Status == EnrollmentStatus.Pending || e.Status == EnrollmentStatus.Approved))
                .Select(e => (EnrollmentStatus?)e.Status)
                .FirstOrDefaultAsync(ct);
            if (active == EnrollmentStatus.Pending)
                throw ApiException.Conflict("Заявка уже на рассмотрении", ErrorCodes.AlreadyPending);
            if (active == EnrollmentStatus.Approved)
                throw ApiException.Conflict("Вы уже записаны", ErrorCodes.AlreadyEnrolled);
            student.FirstName = firstName;
            student.LastName = lastName;
        }
        student.TelegramUsername = request.TelegramUsername;
        student.BotBlockedAt = null;

        var enrollment = db.Enrollments.Add(new Enrollment
        {
            Student = student,
            CourseId = course.Id,
            InviteId = invite.Id,
            Status = EnrollmentStatus.Pending,
            CreatedAt = now,
        }).Entity;
        await db.SaveChangesAsync(ct);
        return new BotApplicationResponse(enrollment.Id, "Заявка отправлена преподавателю. Мы сообщим, когда её рассмотрят");
    }

    /// <summary>С-4.2: ДЗ активных курсов и «Завершённые» (архивные курсы), сначала ближайший дедлайн.</summary>
    [HttpGet("students/{telegramUserId:long}/homeworks")]
    public async Task<BotHomeworksDto> Homeworks(long telegramUserId, CancellationToken ct)
    {
        var (student, facts) = await StudentAsync(telegramUserId, ct);
        var visible = await VisibleAsync(facts, null, ct);
        var latest = (await db.Submissions.AsNoTracking().Include(s => s.Review)
                .Where(s => s.StudentId == student.Id && visible.Select(v => v.Homework.Id).Contains(s.HomeworkId))
                .ToListAsync(ct))
            .GroupBy(s => s.HomeworkId).ToDictionary(g => g.Key, g => g.MaxBy(s => s.Version)!);
        var now = time.GetUtcNow();

        BotHomeworkListItem Item(Homework h)
        {
            var sub = latest.GetValueOrDefault(h.Id);
            var status = Grading.ForStudent(Grading.Cell(true, sub, h.Deadline, now));
            var score = status == BotHomeworkStatus.Reviewed ? sub?.Review?.SentTotal : null;
            return new BotHomeworkListItem(h.Id, h.Subject.Name, h.Title, h.Deadline, status, score);
        }

        var ordered = visible.OrderBy(v => v.Homework.Subject.Name).ThenBy(v => v.Homework.Deadline).ToList();
        return new BotHomeworksDto(
            ordered.Where(v => !v.Finished).Select(v => Item(v.Homework)).ToArray(),
            ordered.Where(v => v.Finished).Select(v => Item(v.Homework)).ToArray());
    }

    /// <summary>С-4.3: карточка ДЗ — условие, версии, результат последней отправленной проверки.</summary>
    [HttpGet("students/{telegramUserId:long}/homeworks/{homeworkId:int}")]
    public async Task<BotHomeworkDto> Homework(long telegramUserId, int homeworkId, CancellationToken ct)
    {
        var (student, facts) = await StudentAsync(telegramUserId, ct);
        var (hw, finished) = await VisibleOneAsync(facts, homeworkId, ct);
        var submissions = await db.Submissions.AsNoTracking().Include(s => s.Review).ThenInclude(r => r!.SentBy)
            .Where(s => s.StudentId == student.Id && s.HomeworkId == homeworkId)
            .OrderBy(s => s.Version)
            .ToListAsync(ct);
        var lastSent = submissions.Where(s => s.Review is { IsSent: true }).MaxBy(s => s.Version);
        BotReviewResultDto? result = lastSent is null
            ? null
            : new BotReviewResultDto(lastSent.Version, lastSent.Review!.SentTotal,
                JsonSerializer.Deserialize<BotSubtaskResult[]>(lastSent.Review.SentSubtasks ?? "[]", JsonSerializerOptions.Web) ?? [],
                lastSent.Review.SentComment, lastSent.Review.SentAt!.Value,
                lastSent.Review.SentBy is { } by ? Notifier.ShortName(by.FullName) : null);

        return new BotHomeworkDto(hw.Id, hw.Subject.Name, hw.Title, hw.Link, hw.Deadline, !finished,
            hw.Subtasks.OrderBy(s => s.Order).Select(s => new SubtaskDto(s.Id, s.Title, s.MaxScore)).ToArray(),
            submissions.Select(s => new BotSubmissionDto(s.Version, s.SubmittedAt, Grading.IsLate(s.SubmittedAt, hw.Deadline))).ToArray(),
            result);
    }

    /// <summary>С-5: сдать PDF (до 20 МБ). Каждая сдача — новая версия; после дедлайна тоже можно.</summary>
    [HttpPost("students/{telegramUserId:long}/homeworks/{homeworkId:int}/submissions")]
    [RequestSizeLimit(Limits.MaxPdfBytes + 1024 * 1024)]
    public async Task<BotSubmitResponse> Submit(long telegramUserId, int homeworkId, IFormFile file, CancellationToken ct)
    {
        var (student, facts) = await StudentAsync(telegramUserId, ct);
        var (hw, finished) = await VisibleOneAsync(facts, homeworkId, ct);
        if (finished)
            throw ApiException.Conflict("Курс завершён, сдача закрыта", ErrorCodes.CourseArchived);
        if (file.Length > Limits.MaxPdfBytes)
            throw ApiException.BadRequest("Файл слишком большой, максимум 20 МБ", ErrorCodes.FileTooLarge);

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            throw ApiException.BadRequest("Нужен файл в формате PDF", ErrorCodes.NotPdf);

        var key = $"submissions/{homeworkId}/{student.Id}/{Guid.NewGuid():N}.pdf";
        await storage.PutAsync(key, bytes, "application/pdf", ct);

        var now = time.GetUtcNow();
        var fileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 300)
            fileName = "работа.pdf";

        // Номер версии под уникальным индексом: при гонке двух сдач повторяем с новым номером
        for (var attempt = 0; ; attempt++)
        {
            var version = (await db.Submissions.Where(s => s.HomeworkId == homeworkId && s.StudentId == student.Id).MaxAsync(s => (int?)s.Version, ct) ?? 0) + 1;
            var submission = db.Submissions.Add(new Submission
            {
                HomeworkId = homeworkId,
                StudentId = student.Id,
                Version = version,
                FileKey = key,
                FileName = fileName,
                FileSize = bytes.Length,
                SubmittedAt = now,
            }).Entity;
            try
            {
                await db.SaveChangesAsync(ct);
                var late = Grading.IsLate(now, hw.Deadline);
                var message = $"Принято: {hw.Subject.Name} — {hw.Title}, версия {version}, {Display.Short(now)}" + (late ? ", сдано после дедлайна" : "");
                return new BotSubmitResponse(version, now, late, message);
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                db.Entry(submission).State = EntityState.Detached;
            }
        }
    }

    private async Task<(Student, StudentFacts)> StudentAsync(long telegramUserId, CancellationToken ct)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.TelegramUserId == telegramUserId, ct)
            ?? throw ApiException.Forbidden("Сначала заполните анкету");
        var facts = await factsLoader.ForStudentAsync(student.Id, ct);
        if (facts.ApprovedCourseIds.Count == 0)
            throw new ApiException(StatusCodes.Status403Forbidden, "Заявка на рассмотрении у преподавателя", ErrorCodes.NotEnrolled);
        if (student.BotBlockedAt is not null)
        {
            student.BotBlockedAt = null;
            await db.SaveChangesAsync(ct);
        }
        return (student, facts);
    }

    private async Task<(Homework, bool)> VisibleOneAsync(StudentFacts facts, int homeworkId, CancellationToken ct)
    {
        var list = await VisibleAsync(facts, homeworkId, ct);
        return list.Count == 1 ? list[0] : throw ApiException.NotFound("ДЗ не найдено");
    }

    /// <summary>Опубликованные ДЗ, адресованные студенту (видимость динамическая, П-7.5). Finished — все его курсы этого ДЗ в архиве.</summary>
    private async Task<List<(Homework Homework, bool Finished)>> VisibleAsync(StudentFacts facts, int? homeworkId, CancellationToken ct)
    {
        var courseIds = facts.ApprovedCourseIds.ToList();
        var query = db.Homeworks.AsNoTracking()
            .Where(h => h.PublishedAt != null && h.Courses.Any(c => courseIds.Contains(c.Id)));
        if (homeworkId is not null)
            query = query.Where(h => h.Id == homeworkId);
        var homeworks = await query
            .Include(h => h.Subject).Include(h => h.Courses).Include(h => h.Targets).Include(h => h.Subtasks)
            .AsSplitQuery()
            .ToListAsync(ct);
        return homeworks
            .Where(h => Recipients.Matches(h.Courses.Select(c => c.Id).ToList(), h.Targets, facts))
            .Select(h => (h, h.Courses.Where(c => facts.ApprovedCourseIds.Contains(c.Id)).All(c => c.IsArchived)))
            .ToList();
    }
}
