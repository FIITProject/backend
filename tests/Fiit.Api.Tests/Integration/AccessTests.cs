using System.Net;
using Fiit.Contracts;

namespace Fiit.Api.Tests.Integration;

/// <summary>Права (Н-4), доступы (П-3), видимость ДЗ только в своих курсах (П-7.1), архив (П-2.5).</summary>
[Collection(ApiCollection.Name)]
public class AccessTests(ApiFactory factory)
{
    [Fact]
    public async Task Duplicate_course_in_same_semester_is_rejected_with_owner_name()
    {
        var (a, _, _) = await factory.TeacherAsync("Ведущий Первый");
        var (b, _, _) = await factory.TeacherAsync();
        var subject = await Scenario.SubjectAsync(a);
        var group = $"ФТ-{ApiFactory.Next()}";
        await Scenario.CourseAsync(a, subject.Id, group, 1);

        var dup = await b.Post("/api/courses", new CreateCourseRequest(subject.Id, $"{group.ToLowerInvariant()}-1", null, Scenario.Semester));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Contains("Ведущий Первый", await dup.Content.ReadAsStringAsync());

        // В другом семестре — новый курс
        var spring = await b.Post("/api/courses", new CreateCourseRequest(subject.Id, group, 1, new SemesterDto(2027, SemesterTerm.Spring))).Ok<CourseDto>();
        Assert.Equal(new SemesterDto(2027, SemesterTerm.Spring), spring.Semester);
    }

    [Fact]
    public async Task Subjects_are_shared_and_deduplicated()
    {
        var (a, _, _) = await factory.TeacherAsync();
        var (b, _, _) = await factory.TeacherAsync();
        var name = $"Тервер {ApiFactory.Next()}";
        var first = await Scenario.SubjectAsync(a, name);
        var second = await Scenario.SubjectAsync(b, $"  {name.ToLowerInvariant()} ");
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task Teacher_sees_only_homeworks_of_own_courses_and_access_grants_it()
    {
        var (a, _, _) = await factory.TeacherAsync();
        var (b, bId, _) = await factory.TeacherAsync();
        var subject = await Scenario.SubjectAsync(a);
        var courseA = await Scenario.CourseAsync(a, subject.Id);
        var courseB = await Scenario.CourseAsync(b, subject.Id);
        var hwA = await a.Post("/api/homeworks", Scenario.Homework(subject.Id, [courseA.Id])).Ok<HomeworkDto>();
        await b.Post("/api/homeworks", Scenario.Homework(subject.Id, [courseB.Id])).Ok<HomeworkDto>();

        var listB = await b.Get<HomeworkDto[]>($"/api/homeworks?subjectId={subject.Id}&semesterYear=2026&semesterTerm=Autumn");
        Assert.DoesNotContain(listB, h => h.Id == hwA.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/homeworks/{hwA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/courses/{courseA.Id}")).StatusCode);

        // Чужой курс нельзя добавить в своё ДЗ
        var foreign = await b.Post("/api/homeworks", Scenario.Homework(subject.Id, [courseB.Id, courseA.Id]));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        // Доступ выдаёт только ведущий
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/api/courses/{courseA.Id}/access/{bId}", null)).StatusCode);
        await a.PostAsync($"/api/courses/{courseA.Id}/access/{bId}", null).Ok();
        listB = await b.Get<HomeworkDto[]>($"/api/homeworks?subjectId={subject.Id}&semesterYear=2026&semesterTerm=Autumn");
        Assert.Contains(listB, h => h.Id == hwA.Id);
        var courseForB = await b.Get<CourseDto>($"/api/courses/{courseA.Id}");
        Assert.False(courseForB.IsAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.DeleteAsync($"/api/courses/{courseA.Id}")).StatusCode);

        await a.DeleteAsync($"/api/courses/{courseA.Id}/access/{bId}").Ok();
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/homeworks/{hwA.Id}")).StatusCode);
    }

    [Fact]
    public async Task Targets_narrow_recipients_and_only_widen_after_publish()
    {
        var (teacher, _, _) = await factory.TeacherAsync();
        var bot = factory.Bot();
        var subject = await Scenario.SubjectAsync(teacher);
        var course = await Scenario.CourseAsync(teacher, subject.Id);
        var invite = await Scenario.InviteAsync(teacher, subject.Id, course.Id);
        var (tg1, s1) = await Scenario.ApprovedStudentAsync(teacher, bot, invite, course.Id, "Алексеев");
        var (tg2, s2) = await Scenario.ApprovedStudentAsync(teacher, bot, invite, course.Id, "Борисов");
        var tag = await teacher.Post("/api/tags", new TagNameRequest($"сильные {ApiFactory.Next()}")).Ok<TagDto>();
        await teacher.Post($"/api/tags/{tag.Id}/students", new TagStudentsRequest([s2])).Ok();

        var request = Scenario.Homework(subject.Id, [course.Id], targets: [new HomeworkTargetDto(HomeworkTargetKind.Tag, tag.Id)]);
        var preview = await teacher.Post("/api/homeworks/preview-recipients", new RecipientsPreviewRequest([course.Id], request.Targets)).Ok<RecipientsPreviewResponse>();
        Assert.Equal(1, preview.Count);
        var hw = await Scenario.PublishedHomeworkAsync(teacher, request);
        Assert.Empty((await bot.Get<BotHomeworksDto>($"/api/bot/students/{tg1}/homeworks")).Active);
        Assert.Single((await bot.Get<BotHomeworksDto>($"/api/bot/students/{tg2}/homeworks")).Active);

        var narrower = await teacher.Put($"/api/homeworks/{hw.Id}", request with { Targets = [new HomeworkTargetDto(HomeworkTargetKind.Student, s1)] });
        Assert.Equal(HttpStatusCode.Conflict, narrower.StatusCode);

        // Расширили до всех — новый адресат получает уведомление о ДЗ, старый — нет
        await Scenario.DrainAsync(bot, 0);
        await teacher.Put($"/api/homeworks/{hw.Id}", request with { Targets = [] }).Ok<HomeworkDto>();
        Assert.Equal(NotificationKind.HomeworkPublished, (await Scenario.DrainAsync(bot, tg1)).Single().Kind);
        Assert.Single((await bot.Get<BotHomeworksDto>($"/api/bot/students/{tg1}/homeworks")).Active);

        var book = await teacher.Get<GradebookDto>($"/api/gradebook?subjectId={subject.Id}&semesterYear=2026&semesterTerm=Autumn&tagId={tag.Id}");
        Assert.Equal("Борисов", book.Rows.Single().LastName);
    }

    [Fact]
    public async Task Rejected_student_can_reapply_and_removed_student_disappears()
    {
        var (teacher, _, _) = await factory.TeacherAsync();
        var bot = factory.Bot();
        var subject = await Scenario.SubjectAsync(teacher);
        var course = await Scenario.CourseAsync(teacher, subject.Id);
        var invite = await Scenario.InviteAsync(teacher, subject.Id, course.Id);
        var tgId = Scenario.NewTelegramId();

        var first = await Scenario.ApplyAsync(bot, tgId, invite, course.Id);
        await teacher.Post("/api/enrollments/reject", new RejectRequest([first], "Не та подгруппа")).Ok();
        var rejected = (await Scenario.DrainAsync(bot, tgId)).Single();
        Assert.Contains("Причина: Не та подгруппа", rejected.Text);
        var state = await bot.Get<BotStudentDto>($"/api/bot/students/{tgId}");
        Assert.Equal(EnrollmentStatus.Rejected, state.Enrollments.Single().Status);

        // С-1.9: повторная анкета известного студента — согласие уже есть
        var second = (await bot.Post("/api/bot/applications", new BotApplicationRequest(tgId, null, invite.Code, course.Id, "Иван", "Иванов", false))
            .Ok<BotApplicationResponse>()).EnrollmentId;
        await teacher.Post("/api/enrollments/approve", new IdsRequest([second])).Ok();
        var dup = await bot.Post("/api/bot/applications", new BotApplicationRequest(tgId, null, invite.Code, course.Id, "Иван", "Иванов", true));
        Assert.Equal(ErrorCodes.AlreadyEnrolled, await dup.ErrorCode());

        await teacher.DeleteAsync($"/api/enrollments/{second}").Ok();
        Assert.Empty(await teacher.Get<EnrollmentDto[]>($"/api/enrollments?courseIds={course.Id}"));
    }

    [Fact]
    public async Task New_student_must_consent_and_names_are_validated()
    {
        var (teacher, _, _) = await factory.TeacherAsync();
        var bot = factory.Bot();
        var subject = await Scenario.SubjectAsync(teacher);
        var course = await Scenario.CourseAsync(teacher, subject.Id);
        var invite = await Scenario.InviteAsync(teacher, subject.Id, course.Id);

        var noConsent = await bot.Post("/api/bot/applications", new BotApplicationRequest(Scenario.NewTelegramId(), null, invite.Code, course.Id, "Иван", "Иванов", false));
        Assert.Equal(ErrorCodes.ConsentRequired, await noConsent.ErrorCode());
        var badName = await bot.Post("/api/bot/applications", new BotApplicationRequest(Scenario.NewTelegramId(), null, invite.Code, course.Id, "Иван1", "Иванов", true));
        Assert.Equal(ErrorCodes.InvalidName, await badName.ErrorCode());

        // С-1.1а: без ссылки — набор по активным приглашениям
        var recruiting = await bot.Get<BotRecruitingDto[]>("/api/bot/recruiting");
        Assert.Contains(recruiting, r => r.SubjectId == subject.Id && r.Courses.Any(c => c.CourseId == course.Id));
        var withoutLink = await bot.Post("/api/bot/applications", new BotApplicationRequest(Scenario.NewTelegramId(), null, null, course.Id, "Иван", "Иванов", true));
        Assert.Equal(HttpStatusCode.OK, withoutLink.StatusCode);

        await teacher.PatchAsync($"/api/invites/{invite.Id}", System.Net.Http.Json.JsonContent.Create(new UpdateInviteRequest(null, true), options: ApiFactory.Json)).Ok<InviteDto>();
        var closed = await bot.Get<BotInviteDto>($"/api/bot/invites/{invite.Code}");
        Assert.False(closed.IsOpen);
        var late = await bot.Post("/api/bot/applications", new BotApplicationRequest(Scenario.NewTelegramId(), null, invite.Code, course.Id, "Иван", "Иванов", true));
        Assert.Equal(ErrorCodes.InviteClosed, await late.ErrorCode());
    }

    [Fact]
    public async Task Archived_course_is_read_only_everywhere()
    {
        var (teacher, _, _) = await factory.TeacherAsync();
        var bot = factory.Bot();
        var subject = await Scenario.SubjectAsync(teacher);
        var course = await Scenario.CourseAsync(teacher, subject.Id);
        var invite = await Scenario.InviteAsync(teacher, subject.Id, course.Id);
        var (tgId, _) = await Scenario.ApprovedStudentAsync(teacher, bot, invite, course.Id);
        var pendingTg = Scenario.NewTelegramId();
        await Scenario.ApplyAsync(bot, pendingTg, invite, course.Id, "Ждущий");
        var hw = await Scenario.PublishedHomeworkAsync(teacher, Scenario.Homework(subject.Id, [course.Id]));
        var submitted = await Scenario.SubmitAsync(bot, tgId, hw.Id);
        await Scenario.DrainAsync(bot, 0);

        await teacher.Post("/api/courses/archive", new CourseIdsRequest([course.Id])).Ok();

        Assert.DoesNotContain(await teacher.Get<CourseDto[]>("/api/courses"), c => c.Id == course.Id);
        Assert.Contains(await teacher.Get<CourseDto[]>("/api/courses?archived=true"), c => c.Id == course.Id);
        Assert.Equal(InviteStatus.Closed, (await teacher.Get<InviteDto[]>("/api/invites")).Single(i => i.Id == invite.Id).Status);
        var rejected = (await Scenario.DrainAsync(bot, pendingTg)).Single();
        Assert.Contains("Набор закрыт", rejected.Text);

        var tasks = await bot.Get<BotHomeworksDto>($"/api/bot/students/{tgId}/homeworks");
        Assert.Empty(tasks.Active);
        Assert.Equal(hw.Id, tasks.Finished.Single().Id);
        using var content = new MultipartFormDataContent { { new ByteArrayContent(TestPdf.Create()), "file", "a.pdf" } };
        var submit = await bot.PostAsync($"/api/bot/students/{tgId}/homeworks/{hw.Id}/submissions", content);
        Assert.Equal(ErrorCodes.CourseArchived, await submit.ErrorCode());

        var book = await teacher.Get<GradebookDto>($"/api/gradebook?subjectId={subject.Id}&semesterYear=2026&semesterTerm=Autumn");
        var cell = book.Rows.Single().Cells.Single();
        Assert.Equal(CellStatus.OnReview, cell.Status);
        var save = await teacher.Put($"/api/submissions/{cell.LatestSubmissionId}/review", new SaveReviewRequest("[]", "x", [], 5));
        Assert.Equal(ErrorCodes.CourseArchived, await save.ErrorCode());
        Assert.Equal(HttpStatusCode.Conflict, (await teacher.Put($"/api/homeworks/{hw.Id}", Scenario.Homework(subject.Id, [course.Id]))).StatusCode);

        await teacher.Post("/api/courses/unarchive", new CourseIdsRequest([course.Id])).Ok();
        await teacher.Put($"/api/submissions/{cell.LatestSubmissionId}/review", new SaveReviewRequest("[]", "x", [], 5)).Ok<ReviewDto>();
        Assert.Equal(1, submitted.Version);
    }
}
