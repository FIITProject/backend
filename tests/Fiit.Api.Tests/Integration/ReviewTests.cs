using System.Net;
using Fiit.Contracts;

namespace Fiit.Api.Tests.Integration;

/// <summary>Одну работу могут проверять разные преподаватели курса; студент видит подпись того, кто отправил.</summary>
[Collection(ApiCollection.Name)]
public class ReviewTests(ApiFactory factory)
{
    [Fact]
    public async Task Any_course_teacher_can_review_and_sender_signs()
    {
        var (lead, _, _) = await factory.TeacherAsync("Ведущая Анна Петровна");
        var (assistant, assistantId, _) = await factory.TeacherAsync("Ассистент Борис Игоревич");
        var bot = factory.Bot();
        var subject = await Scenario.SubjectAsync(lead);
        var course = await Scenario.CourseAsync(lead, subject.Id);
        await lead.PostAsync($"/api/courses/{course.Id}/access/{assistantId}", null).Ok();
        var invite = await Scenario.InviteAsync(lead, subject.Id, course.Id);
        var (tgId, studentId) = await Scenario.ApprovedStudentAsync(lead, bot, invite, course.Id);
        var hw = await Scenario.PublishedHomeworkAsync(lead, Scenario.Homework(subject.Id, [course.Id]));
        await Scenario.SubmitAsync(bot, tgId, hw.Id);
        await Scenario.DrainAsync(bot, 0);

        var context = await lead.Get<ReviewContextDto>($"/api/homeworks/{hw.Id}/students/{studentId}/review");
        var submissionId = context.Submissions.Single().Id;

        // Ведущая начала проверку, ассистент открыл её до этого и сохраняет поверх — конфликт
        var leadDraft = await lead.Put($"/api/submissions/{submissionId}/review", new SaveReviewRequest("[]", "Черновик ведущей", [], 7)).Ok<ReviewDto>();
        var stale = await assistant.Put($"/api/submissions/{submissionId}/review", new SaveReviewRequest("[]", "Поверх", [], 3, DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(ErrorCodes.ReviewChangedByColleague, await stale.ErrorCode());

        // Перечитал актуальный черновик — может продолжить и отправить сам
        var fresh = await assistant.Put($"/api/submissions/{submissionId}/review",
            new SaveReviewRequest("[]", "Доработал ассистент", [], 8, leadDraft.UpdatedAt)).Ok<ReviewDto>();
        Assert.Equal("Ассистент Борис Игоревич", fresh.UpdatedByName);
        var sent = await assistant.PostAsync($"/api/submissions/{submissionId}/review/send", null).Ok<ReviewDto>();
        Assert.Equal("Ассистент Борис Игоревич", sent.SentByName);

        var notice = (await Scenario.DrainAsync(bot, tgId)).Single();
        Assert.Contains("Проверил(а): Ассистент Б. И.", notice.Text);
        var card = await bot.Get<BotHomeworkDto>($"/api/bot/students/{tgId}/homeworks/{hw.Id}");
        Assert.Equal("Ассистент Б. И.", card.LastResult!.ReviewedBy);

        // Ведущая правит и переотправляет — подпись меняется на неё
        await lead.Put($"/api/submissions/{submissionId}/review", new SaveReviewRequest("[]", "Финал", [], 9, sent.UpdatedAt)).Ok<ReviewDto>();
        await lead.PostAsync($"/api/submissions/{submissionId}/review/send", null).Ok<ReviewDto>();
        Assert.Contains("Проверил(а): Ведущая А. П.", (await Scenario.DrainAsync(bot, tgId)).Single().Text);
        var book = await lead.Get<GradebookDto>($"/api/gradebook?subjectId={subject.Id}&semesterYear=2026&semesterTerm=Autumn");
        Assert.Equal("Ведущая А. П.", book.Rows.Single().Cells.Single().ReviewedBy);
    }
}
