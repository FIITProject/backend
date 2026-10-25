using System.Net;
using Fiit.Contracts;
using PdfSharp.Pdf.IO;

namespace Fiit.Api.Tests.Integration;

/// <summary>Сквозные сценарии Б–Д: сбор студентов, выдача ДЗ, сдача, проверка.</summary>
[Collection(ApiCollection.Name)]
public class FlowTests(ApiFactory factory)
{
    [Fact]
    public async Task Full_cycle_from_invite_to_review()
    {
        var (teacher, _, _) = await factory.TeacherAsync("Сидоров Сидор Сидорович");
        var bot = factory.Bot();
        var subject = await Scenario.SubjectAsync(teacher);
        var course = await Scenario.CourseAsync(teacher, subject.Id, "фт 777", 1);
        Assert.Equal("ФТ-777-1", course.SubgroupName);
        Assert.True(course.IsAdmin);

        // Б: приглашение → анкета в боте → подтверждение
        var invite = await Scenario.InviteAsync(teacher, subject.Id, course.Id);
        Assert.Equal($"https://t.me/test_bot?start={invite.Code}", invite.Link);
        var botInvite = await bot.Get<BotInviteDto>($"/api/bot/invites/{invite.Code}");
        Assert.True(botInvite.IsOpen);
        Assert.Equal("ФТ-777-1", botInvite.Courses.Single().Name);

        var tgId = Scenario.NewTelegramId();
        var enrollmentId = await Scenario.ApplyAsync(bot, tgId, invite, course.Id, "Петрова", "Анна");
        var again = await bot.Post("/api/bot/applications", new BotApplicationRequest(tgId, null, invite.Code, course.Id, "Анна", "Петрова", true));
        Assert.Equal(ErrorCodes.AlreadyPending, await again.ErrorCode());

        var forbidden = await bot.GetAsync($"/api/bot/students/{tgId}/homeworks");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var pending = await teacher.Get<EnrollmentDto[]>($"/api/enrollments?courseIds={course.Id}&status=Pending");
        Assert.Equal("Петрова", pending.Single().LastName);
        await teacher.Post("/api/enrollments/approve", new IdsRequest([enrollmentId])).Ok();
        var approved = (await Scenario.DrainAsync(bot, tgId)).Single();
        Assert.Equal(NotificationKind.ApplicationApproved, approved.Kind);
        Assert.Contains("преподаватель Сидоров С. С.", approved.Text);

        var student = await bot.Get<BotStudentDto>($"/api/bot/students/{tgId}");
        Assert.True(student.HasApprovedCourse);

        // В: ДЗ с подзадачами → публикация → уведомление
        var request = Scenario.Homework(subject.Id, [course.Id], DateTimeOffset.UtcNow.AddDays(3),
            [new SubtaskDto(null, "Задача 1", 5), new SubtaskDto(null, "Задача 2", 5)]);
        var hw = await teacher.Post("/api/homeworks", request).Ok<HomeworkDto>();
        Assert.False(hw.IsPublished);
        Assert.Equal(1, hw.RecipientsCount);
        Assert.Empty((await bot.Get<BotHomeworksDto>($"/api/bot/students/{tgId}/homeworks")).Active);

        var published = await teacher.PostAsync($"/api/homeworks/{hw.Id}/publish", null).Ok<PublishResponse>();
        Assert.Equal(1, published.Notified);
        var hwNotice = (await Scenario.DrainAsync(bot, tgId)).Single();
        Assert.Equal(NotificationKind.HomeworkPublished, hwNotice.Kind);
        Assert.Equal(hw.Id, hwNotice.HomeworkId);

        // Г: сдача двух версий
        var tasks = await bot.Get<BotHomeworksDto>($"/api/bot/students/{tgId}/homeworks");
        Assert.Equal(BotHomeworkStatus.NotSubmitted, tasks.Active.Single().Status);
        var v1 = await Scenario.SubmitAsync(bot, tgId, hw.Id);
        Assert.Equal(1, v1.Version);
        Assert.False(v1.IsLate);
        var v2 = await Scenario.SubmitAsync(bot, tgId, hw.Id, TestPdf.Create(pages: 2));
        Assert.Equal(2, v2.Version);
        Assert.StartsWith($"Принято: {subject.Name} — {request.Title}, версия 2", v2.Message);

        var book = await teacher.Get<GradebookDto>($"/api/gradebook?subjectId={subject.Id}&semesterYear=2026&semesterTerm=Autumn");
        var cell = book.Rows.Single().Cells.Single();
        Assert.Equal(CellStatus.OnReview, cell.Status);
        Assert.Equal(2, cell.Versions);

        // Д: проверка последней версии и отправка
        var context = await teacher.Get<ReviewContextDto>($"/api/homeworks/{hw.Id}/students/{book.Rows.Single().StudentId}/review");
        Assert.Equal(2, context.Submissions.Length);
        var latest = context.Submissions.Single(s => s.IsLatest);
        var old = context.Submissions.Single(s => !s.IsLatest);
        var subtaskIds = context.Subtasks.Select(s => s.Id!.Value).ToArray();

        var tooHigh = await teacher.Put($"/api/submissions/{latest.Id}/review", new SaveReviewRequest("[]", null, [new SubtaskScoreDto(subtaskIds[0], 6)], null));
        Assert.Equal(HttpStatusCode.BadRequest, tooHigh.StatusCode);
        var oldVersion = await teacher.Put($"/api/submissions/{old.Id}/review", new SaveReviewRequest("[]", null, [], null));
        Assert.Equal(HttpStatusCode.Conflict, oldVersion.StatusCode);

        const string annotations = """[{"type":"stroke","page":1,"color":"#d00000","width":2,"opacity":1,"points":[10,10,40,40]},{"type":"text","page":0,"x":20,"y":30,"text":"Проверь знак","color":"#0000ff","size":12}]""";
        var draft = await teacher.Put($"/api/submissions/{latest.Id}/review",
            new SaveReviewRequest(annotations, "Хорошо", [new SubtaskScoreDto(subtaskIds[0], 4.5m), new SubtaskScoreDto(subtaskIds[1], 5)], null)).Ok<ReviewDto>();
        Assert.Equal(9.5m, draft.Total);
        Assert.Null(draft.SentAt);
        // повторное сохранение того же черновика (автосохранение) не ломает баллы
        draft = await teacher.Put($"/api/submissions/{latest.Id}/review",
            new SaveReviewRequest(annotations, "Хорошо", [new SubtaskScoreDto(subtaskIds[0], 4.5m), new SubtaskScoreDto(subtaskIds[1], 5)], null)).Ok<ReviewDto>();
        Assert.Equal(9.5m, draft.Total);

        var sent = await teacher.PostAsync($"/api/submissions/{latest.Id}/review/send", null).Ok<ReviewDto>();
        Assert.NotNull(sent.SentAt);
        Assert.False(sent.HasUnsentChanges);

        var result = (await Scenario.DrainAsync(bot, tgId)).Single();
        Assert.Equal(NotificationKind.ReviewSent, result.Kind);
        Assert.Contains("Оценка: 9.5", result.Text);
        Assert.Contains("Комментарий: Хорошо", result.Text);
        Assert.True(result.HasFile);
        var pdf = await bot.GetByteArrayAsync($"/api/bot/notifications/{result.Id}/file");
        using (var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import))
            Assert.Equal(2, doc.PageCount);

        var card = await bot.Get<BotHomeworkDto>($"/api/bot/students/{tgId}/homeworks/{hw.Id}");
        Assert.Equal(2, card.LastResult!.Version);
        Assert.Equal(9.5m, card.LastResult.Total);
        Assert.Equal(4.5m, card.LastResult.Subtasks[0].Score);

        book = await teacher.Get<GradebookDto>($"/api/gradebook?subjectId={subject.Id}&semesterYear=2026&semesterTerm=Autumn");
        Assert.Equal(CellStatus.Reviewed, book.Rows.Single().Cells.Single().Status);
        Assert.Equal(9.5m, book.Rows.Single().Average);

        // П-9.8: правки после отправки → «есть неотправленные правки», повторная отправка → «Проверка обновлена»
        await teacher.Put($"/api/submissions/{latest.Id}/review", new SaveReviewRequest(annotations, "Отлично", [new SubtaskScoreDto(subtaskIds[0], 5), new SubtaskScoreDto(subtaskIds[1], 5)], null)).Ok<ReviewDto>();
        book = await teacher.Get<GradebookDto>($"/api/gradebook?subjectId={subject.Id}&semesterYear=2026&semesterTerm=Autumn");
        Assert.Equal(CellStatus.ReviewedWithUnsentChanges, book.Rows.Single().Cells.Single().Status);
        Assert.Equal(9.5m, book.Rows.Single().Cells.Single().Score);
        await teacher.PostAsync($"/api/submissions/{latest.Id}/review/send", null).Ok<ReviewDto>();
        Assert.StartsWith("Проверка обновлена", (await Scenario.DrainAsync(bot, tgId)).Single().Text);

        // Подзадачи с баллами менять нельзя
        var edit = await teacher.Put($"/api/homeworks/{hw.Id}", request with { Subtasks = [new SubtaskDto(subtaskIds[0], "Задача 1", 10)] });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);

        // Новая версия → снова «На проверке»
        await Scenario.SubmitAsync(bot, tgId, hw.Id);
        book = await teacher.Get<GradebookDto>($"/api/gradebook?subjectId={subject.Id}&semesterYear=2026&semesterTerm=Autumn");
        Assert.Equal(CellStatus.OnReview, book.Rows.Single().Cells.Single().Status);

        var xlsx = await teacher.GetAsync($"/api/gradebook/export.xlsx?subjectId={subject.Id}&semesterYear=2026&semesterTerm=Autumn");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType?.MediaType);
        Assert.True((await xlsx.Content.ReadAsByteArrayAsync()).Length > 1000);
    }

    [Fact]
    public async Task Submission_validation_and_lateness()
    {
        var (teacher, _, _) = await factory.TeacherAsync();
        var bot = factory.Bot();
        var subject = await Scenario.SubjectAsync(teacher);
        var course = await Scenario.CourseAsync(teacher, subject.Id);
        var invite = await Scenario.InviteAsync(teacher, subject.Id, course.Id);
        var (tgId, _) = await Scenario.ApprovedStudentAsync(teacher, bot, invite, course.Id);
        var hw = await Scenario.PublishedHomeworkAsync(teacher, Scenario.Homework(subject.Id, [course.Id], DateTimeOffset.UtcNow.AddMinutes(-5)));

        using var notPdf = new MultipartFormDataContent { { new ByteArrayContent("hello"u8.ToArray()), "file", "a.pdf" } };
        var response = await bot.PostAsync($"/api/bot/students/{tgId}/homeworks/{hw.Id}/submissions", notPdf);
        Assert.Equal(ErrorCodes.NotPdf, await response.ErrorCode());

        var late = await Scenario.SubmitAsync(bot, tgId, hw.Id);
        Assert.True(late.IsLate);
        Assert.EndsWith("сдано после дедлайна", late.Message);

        // П-7.6: продлили дедлайн — сдача перестаёт быть опоздавшей, студент получает уведомление
        await Scenario.DrainAsync(bot, tgId);
        var full = await teacher.Get<HomeworkDto>($"/api/homeworks/{hw.Id}");
        await teacher.Put($"/api/homeworks/{hw.Id}", new HomeworkEditRequest(subject.Id, Scenario.Semester, full.Title, full.Link,
            DateTimeOffset.UtcNow.AddDays(1), [], [course.Id], [])).Ok<HomeworkDto>();
        var changed = (await Scenario.DrainAsync(bot, tgId)).Single();
        Assert.Equal(NotificationKind.HomeworkChanged, changed.Kind);
        Assert.Contains("Дедлайн изменён", changed.Text);
        var card = await bot.Get<BotHomeworkDto>($"/api/bot/students/{tgId}/homeworks/{hw.Id}");
        Assert.False(card.Versions.Single().IsLate);
    }
}
