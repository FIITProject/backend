using System.Net;
using System.Net.Http.Headers;
using Fiit.Api.Services;
using Fiit.Contracts;

namespace Fiit.Api.Tests.Integration;

/// <summary>С-5.2: опасный или непроверенный PDF не сохраняется, бот получает понятный текст и код.</summary>
[Collection(ApiCollection.Name)]
public class SubmissionSafetyTests(ApiFactory factory)
{
    [Fact]
    public async Task Pdf_with_script_is_rejected()
    {
        var (bot, tgId, hwId) = await StudentWithHomeworkAsync();

        var response = await SubmitRawAsync(bot, tgId, hwId, TestPdf.Raw("/OpenAction << /S /JavaScript /JS (app.alert(1)) >>"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.PdfUnsafe, await response.ErrorCode());
        Assert.Empty((await bot.Get<BotHomeworkDto>($"/api/bot/students/{tgId}/homeworks/{hwId}")).Versions);
    }

    [Fact]
    public async Task Fake_pdf_is_rejected()
    {
        var (bot, tgId, hwId) = await StudentWithHomeworkAsync();

        var response = await SubmitRawAsync(bot, tgId, hwId, "%PDF-1.4\n<script>alert(1)</script>"u8.ToArray());

        Assert.Equal(ErrorCodes.NotPdf, await response.ErrorCode());
    }

    [Fact]
    public async Task Infected_pdf_is_rejected_and_unavailable_scanner_fails_closed()
    {
        var (bot, tgId, hwId) = await StudentWithHomeworkAsync();
        try
        {
            factory.Scanner.Verdict = _ => new ScanResult(false, "Win.Test.EICAR_HDB-1");
            var infected = await SubmitRawAsync(bot, tgId, hwId, TestPdf.Create());
            Assert.Equal(HttpStatusCode.BadRequest, infected.StatusCode);
            Assert.Equal(ErrorCodes.PdfInfected, await infected.ErrorCode());

            factory.Scanner.Verdict = _ => throw new MalwareScanUnavailableException("clamd is down");
            var unavailable = await SubmitRawAsync(bot, tgId, hwId, TestPdf.Create());
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal(ErrorCodes.ScanUnavailable, await unavailable.ErrorCode());
        }
        finally
        {
            factory.Scanner.Verdict = _ => new ScanResult(true);
        }
        Assert.Empty((await bot.Get<BotHomeworkDto>($"/api/bot/students/{tgId}/homeworks/{hwId}")).Versions);

        Assert.Equal(1, (await Scenario.SubmitAsync(bot, tgId, hwId)).Version);
    }

    private async Task<(HttpClient Bot, long TgId, int HomeworkId)> StudentWithHomeworkAsync()
    {
        var (teacher, _, _) = await factory.TeacherAsync();
        var bot = factory.Bot();
        var subject = await Scenario.SubjectAsync(teacher);
        var course = await Scenario.CourseAsync(teacher, subject.Id);
        var invite = await Scenario.InviteAsync(teacher, subject.Id, course.Id);
        var (tgId, _) = await Scenario.ApprovedStudentAsync(teacher, bot, invite, course.Id);
        var hw = await Scenario.PublishedHomeworkAsync(teacher, Scenario.Homework(subject.Id, [course.Id]));
        return (bot, tgId, hw.Id);
    }

    private static async Task<HttpResponseMessage> SubmitRawAsync(HttpClient bot, long tgId, int homeworkId, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "работа.pdf");
        return await bot.PostAsync($"/api/bot/students/{tgId}/homeworks/{homeworkId}/submissions", content);
    }
}
