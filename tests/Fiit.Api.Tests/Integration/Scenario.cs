using System.Net.Http.Headers;
using Fiit.Contracts;

namespace Fiit.Api.Tests.Integration;

/// <summary>Шаги сквозных сценариев ТЗ (А–Д) через публичное API — переиспользуются в тестах.</summary>
public static class Scenario
{
    public static readonly SemesterDto Semester = new(2026, SemesterTerm.Autumn);

    public static async Task<SubjectDto> SubjectAsync(HttpClient teacher, string? name = null) =>
        await teacher.Post("/api/subjects", new CreateSubjectRequest(name ?? $"Предмет {ApiFactory.Next()}")).Ok<SubjectDto>();

    public static async Task<CourseDto> CourseAsync(HttpClient teacher, int subjectId, string? group = null, int? subgroup = 1) =>
        await teacher.Post("/api/courses", new CreateCourseRequest(subjectId, group ?? $"ФТ-{ApiFactory.Next()}", subgroup, Semester)).Ok<CourseDto>();

    public static async Task<InviteDto> InviteAsync(HttpClient teacher, int subjectId, params int[] courseIds) =>
        await teacher.Post("/api/invites", new CreateInviteRequest(subjectId, courseIds, null)).Ok<InviteDto>();

    public static long NewTelegramId() => 1_000_000 + ApiFactory.Next();

    public static async Task<int> ApplyAsync(HttpClient bot, long tgId, InviteDto invite, int courseId, string lastName = "Иванов", string firstName = "Иван") =>
        (await bot.Post("/api/bot/applications", new BotApplicationRequest(tgId, $"user{tgId}", invite.Code, courseId, firstName, lastName, true))
            .Ok<BotApplicationResponse>()).EnrollmentId;

    /// <summary>Студент подал заявку по приглашению и его подтвердили.</summary>
    public static async Task<(long TgId, int StudentId)> ApprovedStudentAsync(HttpClient teacher, HttpClient bot, InviteDto invite, int courseId, string lastName = "Иванов")
    {
        var tgId = NewTelegramId();
        var enrollmentId = await ApplyAsync(bot, tgId, invite, courseId, lastName);
        await teacher.Post("/api/enrollments/approve", new IdsRequest([enrollmentId])).Ok();
        var list = await teacher.Get<EnrollmentDto[]>($"/api/enrollments?courseIds={courseId}");
        return (tgId, list.Single(e => e.Id == enrollmentId).StudentId);
    }

    public static HomeworkEditRequest Homework(int subjectId, int[] courseIds, DateTimeOffset? deadline = null, SubtaskDto[]? subtasks = null, HomeworkTargetDto[]? targets = null) =>
        new(subjectId, Semester, $"ДЗ {ApiFactory.Next()}", "https://example.com/task", deadline ?? DateTimeOffset.UtcNow.AddDays(7),
            subtasks ?? [], courseIds, targets ?? []);

    public static async Task<HomeworkDto> PublishedHomeworkAsync(HttpClient teacher, HomeworkEditRequest request)
    {
        var hw = await teacher.Post("/api/homeworks", request).Ok<HomeworkDto>();
        await teacher.PostAsync($"/api/homeworks/{hw.Id}/publish", null).Ok<PublishResponse>();
        return hw;
    }

    public static async Task<BotSubmitResponse> SubmitAsync(HttpClient bot, long tgId, int homeworkId, byte[]? pdf = null)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(pdf ?? TestPdf.Create());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "решение.pdf");
        return await bot.PostAsync($"/api/bot/students/{tgId}/homeworks/{homeworkId}/submissions", content).Ok<BotSubmitResponse>();
    }

    /// <summary>Как бот: забрать всё из очереди и подтвердить; вернуть то, что адресовано студенту.</summary>
    public static async Task<List<BotNotificationDto>> DrainAsync(HttpClient bot, long tgId)
    {
        var mine = new List<BotNotificationDto>();
        while (true)
        {
            var batch = await bot.Post("/api/bot/notifications/claim", new ClaimRequest(100)).Ok<BotNotificationDto[]>();
            if (batch.Length == 0)
                return mine;
            foreach (var n in batch)
                await bot.PostAsync($"/api/bot/notifications/{n.Id}/ack", null).Ok();
            mine.AddRange(batch.Where(n => n.TelegramUserId == tgId));
        }
    }
}
