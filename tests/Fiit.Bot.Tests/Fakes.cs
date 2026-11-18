using System.Net;
using Fiit.Bot.Core;
using Fiit.Contracts;

namespace Fiit.Bot.Tests;

/// <summary>Сообщения, которые бот «отправил» в чат.</summary>
public class RecordingOutput : IChatOutput
{
    public List<(long ChatId, BotMessage Message)> Sent { get; } = [];
    public Func<long, BotMessage, Exception?> Fail { get; set; } = (_, _) => null;

    public BotMessage Last => Sent[^1].Message;
    public IEnumerable<string> Texts => Sent.Select(s => s.Message.Text);

    public Task SendAsync(long chatId, BotMessage message, CancellationToken ct)
    {
        if (Fail(chatId, message) is { } e)
            throw e;
        Sent.Add((chatId, message));
        return Task.CompletedTask;
    }
}

/// <summary>Бэкенд в памяти: один предмет с двумя подгруппами, одно ДЗ.</summary>
public class FakeApi : IBotApi
{
    public static readonly BotCourseOption[] Courses = [new(10, "ФТ-201-1"), new(11, "ФТ-201-2")];

    public BotStudentDto Student { get; set; } = new(false, null, null, false, false, []);
    public bool InviteOpen { get; set; } = true;
    public BotRecruitingDto[] Recruiting { get; set; } = [new(1, "Python", 7, "Иванов И. И.", Courses)];
    public BotHomeworkListItem[] Active { get; set; } = [new(100, "Python", "ДЗ 1", DateTimeOffset.UtcNow.AddDays(3), BotHomeworkStatus.NotSubmitted, null)];
    public List<BotApplicationRequest> Applications { get; } = [];
    public List<(int HomeworkId, byte[] Pdf, string Name)> Submissions { get; } = [];
    public BotApiException? SubmitError { get; set; }
    public Dictionary<int, NotificationStatus> NotificationStates { get; } = [];
    public List<int> Delivered { get; } = [];
    public List<(int Id, BotDeliveryFailedRequest Request)> Failed { get; } = [];

    public Task<BotInviteDto> InviteAsync(string code, CancellationToken ct) => code == "good"
        ? Task.FromResult(new BotInviteDto(code, InviteOpen, 1, "Python", "Иванов И. И.", new SemesterDto(2026, SemesterTerm.Autumn), Courses))
        : throw new BotApiException(HttpStatusCode.NotFound, "Приглашение не найдено", null);

    public Task<BotRecruitingDto[]> RecruitingAsync(CancellationToken ct) => Task.FromResult(Recruiting);
    public Task<BotStudentDto> StudentAsync(long telegramUserId, CancellationToken ct) => Task.FromResult(Student);

    public Task<BotApplicationResponse> ApplyAsync(BotApplicationRequest request, CancellationToken ct)
    {
        Applications.Add(request);
        return Task.FromResult(new BotApplicationResponse(1, "Заявка отправлена преподавателю. Мы сообщим, когда её рассмотрят"));
    }

    public Task<BotHomeworksDto> HomeworksAsync(long telegramUserId, CancellationToken ct) => Task.FromResult(new BotHomeworksDto(Active, []));

    public Task<BotHomeworkDto> HomeworkAsync(long telegramUserId, int homeworkId, CancellationToken ct) =>
        Task.FromResult(new BotHomeworkDto(homeworkId, "Python", "ДЗ 1", "https://example.com", DateTimeOffset.UtcNow.AddDays(3), true, [], [], null));

    public Task<BotSubmitResponse> SubmitAsync(long telegramUserId, int homeworkId, byte[] pdf, string fileName, CancellationToken ct)
    {
        if (SubmitError is not null)
            throw SubmitError;
        Submissions.Add((homeworkId, pdf, fileName));
        return Task.FromResult(new BotSubmitResponse(Submissions.Count, DateTimeOffset.UtcNow, false, $"Принято: Python — ДЗ 1, версия {Submissions.Count}"));
    }

    public Task<BotNotificationStateDto> NotificationStateAsync(int id, CancellationToken ct) =>
        Task.FromResult(new BotNotificationStateDto(id, NotificationStates.GetValueOrDefault(id, NotificationStatus.Published)));

    public Task NotificationDeliveredAsync(int id, CancellationToken ct)
    {
        Delivered.Add(id);
        return Task.CompletedTask;
    }

    public Task NotificationFailedAsync(int id, BotDeliveryFailedRequest request, CancellationToken ct)
    {
        Failed.Add((id, request));
        return Task.CompletedTask;
    }

    public Task<byte[]> NotificationFileAsync(int id, CancellationToken ct) => Task.FromResult<byte[]>([1, 2, 3]);
    public Task<BotNotificationDto[]> StudentNotificationsAsync(long telegramUserId, int afterId, CancellationToken ct) => Task.FromResult<BotNotificationDto[]>([]);

    public static BotStudentDto Approved => new(true, "Анна", "Петрова", true, true,
        [new BotEnrollmentDto(10, "Python", "ФТ-201-1", "Иванов И. И.", EnrollmentStatus.Approved, null)]);
}
