using System.Net;
using Fiit.Contracts;

namespace Fiit.Api.Tests.Integration;

/// <summary>Колокольчик в реальном времени: открытая вкладка получает событие, как только студент подал заявку или сдал работу.</summary>
[Collection(ApiCollection.Name)]
public class LiveEventsTests(ApiFactory factory)
{
    [Fact]
    public async Task Open_tab_gets_events_on_application_and_submission()
    {
        var (teacher, _, _) = await factory.TeacherAsync();
        var bot = factory.Bot();
        var subject = await Scenario.SubjectAsync(teacher);
        var course = await Scenario.CourseAsync(teacher, subject.Id);
        var invite = await Scenario.InviteAsync(teacher, subject.Id, course.Id);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me/events");
        using var response = await teacher.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));
        Assert.Equal("ready", await NextEventAsync(reader, timeout.Token));

        var (tgId, _) = await Scenario.ApprovedStudentAsync(teacher, bot, invite, course.Id);
        Assert.Equal("applications", await NextChangeAsync(reader, "applications", timeout.Token));

        var hw = await Scenario.PublishedHomeworkAsync(teacher, Scenario.Homework(subject.Id, [course.Id]));
        await Scenario.SubmitAsync(bot, tgId, hw.Id);
        Assert.Equal("submissions", await NextChangeAsync(reader, "submissions", timeout.Token));
    }

    [Fact]
    public async Task Events_require_teacher_session()
    {
        var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync("/api/me/events");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Имя следующего события (строки <c>event:</c>); комментарии-пинги пропускаются.</summary>
    private static async Task<string> NextEventAsync(StreamReader reader, CancellationToken ct)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(ct) ?? throw new EndOfStreamException();
            if (line.StartsWith("event: ")) return line["event: ".Length..];
        }
    }

    /// <summary>Ждёт <c>changed</c> с нужным видом: события других тестов в общей фабрике пропускаются.</summary>
    private static async Task<string> NextChangeAsync(StreamReader reader, string kind, CancellationToken ct)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(ct) ?? throw new EndOfStreamException();
            if (line == $"data: {kind}") return kind;
        }
    }
}
