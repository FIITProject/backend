using System.Net;
using Fiit.Contracts;

namespace Fiit.Web.Services;

public record ChatButton(string Text, Func<Task> Action);

public record ChatMessage(bool FromBot, string Text, IReadOnlyList<ChatButton> Buttons, int? FileNotificationId = null, string? FileName = null)
{
    public DateTime At { get; } = DateTime.Now;
}

/// <summary>Диалог одного студента с ботом: лог сообщений и шаг анкеты.</summary>
public class StudentChat(long telegramUserId, string username)
{
    public long TelegramUserId { get; } = telegramUserId;
    public string Username { get; } = username;
    public List<ChatMessage> Messages { get; } = [];
    public string Title { get; set; } = $"tg {telegramUserId}";

    internal Step Step { get; set; } = Step.Idle;
    internal string? InviteCode { get; set; }
    internal string SubjectName { get; set; } = "";
    internal BotCourseOption[] Courses { get; set; } = [];
    internal int? CourseId { get; set; }
    internal string? LastName { get; set; }
    internal string? FirstName { get; set; }
    internal bool Known { get; set; }
    internal int? AwaitingPdfFor { get; set; }
    internal (byte[] Bytes, string Name)? PendingPdf { get; set; }
}

internal enum Step
{
    Idle,
    ConfirmName,
    LastName,
    FirstName,
    Subgroup,
    Consent,
    Summary,
    WaitingPdf,
}

/// <summary>
/// Эмулятор Telegram-бота для теста без Telegram: повторяет сценарии раздела 4 ТЗ поверх /api/bot/*.
/// Логику переносим в настоящего бота на C# один к одному.
/// </summary>
public class BotEmulator(BotApi api)
{
    public List<StudentChat> Chats { get; } = [];
    public StudentChat? Current { get; set; }
    public event Action? Changed;

    public StudentChat NewStudent(long? tgId = null, string? username = null)
    {
        var id = tgId ?? Random.Shared.NextInt64(100_000_000, 999_999_999);
        var chat = Chats.FirstOrDefault(c => c.TelegramUserId == id);
        if (chat is null)
        {
            chat = new StudentChat(id, username ?? $"student{id % 10000}");
            Chats.Add(chat);
        }
        Current = chat;
        Changed?.Invoke();
        return chat;
    }

    // ── Команды студента ──────────────────────────────────────────────

    /// <summary>/start [код] — С-1.1, С-1.1а.</summary>
    public Task Start(StudentChat chat, string? code) => Guard(chat, async () =>
    {
        User(chat, string.IsNullOrWhiteSpace(code) ? "/start" : $"/start {code}");
        Reset(chat);
        var student = await api.Student(chat.TelegramUserId);
        UpdateTitle(chat, student);

        if (!string.IsNullOrWhiteSpace(code))
        {
            BotInviteDto invite;
            try
            {
                invite = await api.Invite(code.Trim());
            }
            catch (ApiError e) when (e.Status == HttpStatusCode.NotFound)
            {
                Bot(chat, "Приглашение не найдено, попросите у преподавателя ссылку");
                return;
            }
            if (!invite.IsOpen)
            {
                Bot(chat, "Набор закрыт, обратитесь к преподавателю");
                return;
            }
            await BeginApplication(chat, student, invite.Code, invite.SubjectName, invite.TeacherName, invite.Courses);
            return;
        }

        if (student.HasApprovedCourse)
        {
            Bot(chat, $"Здравствуйте, {student.FirstName}! Команды: /tasks — мои задания, /help — справка.", [MyTasksButton(chat)]);
            return;
        }

        var recruiting = await api.Recruiting();
        if (recruiting.Length == 0)
        {
            Bot(chat, "Сейчас нет открытых наборов, попросите у преподавателя ссылку");
            return;
        }
        Bot(chat, "Выберите предмет:", recruiting.Select(r => new ChatButton($"{r.SubjectName} — {r.TeacherName}", () => Guard(chat, async () =>
        {
            User(chat, $"{r.SubjectName} — {r.TeacherName}");
            await BeginApplication(chat, await api.Student(chat.TelegramUserId), null, r.SubjectName, r.TeacherName, r.Courses);
        }))).ToArray());
    });

    /// <summary>Текстовое сообщение студента: ответ на шаг анкеты или команда.</summary>
    public Task Text(StudentChat chat, string text) => Guard(chat, async () =>
    {
        text = text.Trim();
        if (text.Length == 0)
            return;
        if (text.StartsWith("/start"))
        {
            await Start(chat, text.Length > 6 ? text[6..].Trim() : null);
            return;
        }
        User(chat, text);

        switch (chat.Step)
        {
            case Step.LastName:
                chat.LastName = text;
                chat.Step = Step.FirstName;
                Bot(chat, "Ваше имя?");
                return;
            case Step.FirstName:
                chat.FirstName = text;
                AskSubgroup(chat);
                return;
        }

        var student = await api.Student(chat.TelegramUserId);
        if (!student.HasApprovedCourse)
        {
            Bot(chat, student.Enrollments.Any(e => e.Status == EnrollmentStatus.Pending) ? "Заявка на рассмотрении у преподавателя" : "Сначала заполните анкету");
            return;
        }

        switch (text)
        {
            case "/tasks":
                await ShowTasks(chat);
                return;
            case "/help":
                Bot(chat, "/tasks — мои задания\n/start — приветствие или запись по ссылке\nЧтобы сдать ДЗ — откройте его в «Мои задания» и нажмите «Сдать».");
                return;
            default:
                Bot(chat, "Не понял. /tasks — мои задания, /help — справка", [MyTasksButton(chat)]);
                return;
        }
    });

    /// <summary>Студент прислал файл — С-5.1, С-5.5.</summary>
    public Task File(StudentChat chat, byte[] bytes, string name) => Guard(chat, async () =>
    {
        User(chat, $"📎 {name} ({bytes.Length / 1024} КБ)");
        // Как бот: размер проверяем до скачивания
        if (bytes.Length > 20 * 1024 * 1024)
        {
            Bot(chat, "Файл слишком большой, максимум 20 МБ");
            return;
        }
        if (chat.Step == Step.WaitingPdf && chat.AwaitingPdfFor is { } hwId)
        {
            await Submit(chat, hwId, bytes, name);
            return;
        }

        var student = await api.Student(chat.TelegramUserId);
        if (!student.HasApprovedCourse)
        {
            Bot(chat, "Сначала заполните анкету");
            return;
        }
        var tasks = await api.Homeworks(chat.TelegramUserId);
        if (tasks.Active.Length == 0)
        {
            Bot(chat, "Нет открытых заданий для сдачи");
            return;
        }
        chat.PendingPdf = (bytes, name);
        Bot(chat, "К какому заданию отнести файл?", tasks.Active.Select(t => new ChatButton($"{t.SubjectName}: {t.Title}", () => Guard(chat, async () =>
        {
            User(chat, t.Title);
            if (chat.PendingPdf is { } pdf)
                await Submit(chat, t.Id, pdf.Bytes, pdf.Name);
            chat.PendingPdf = null;
        }))).ToArray());
    });

    /// <summary>Как цикл бота: забрать уведомления из outbox, «отправить» в чаты и подтвердить.</summary>
    public async Task<int> Poll()
    {
        var batch = await api.Claim();
        foreach (var n in batch)
        {
            var chat = Chats.FirstOrDefault(c => c.TelegramUserId == n.TelegramUserId) ?? NewStudentSilently(n.TelegramUserId);
            var buttons = new List<ChatButton>();
            switch (n.Kind)
            {
                case NotificationKind.ApplicationApproved:
                    buttons.Add(MyTasksButton(chat));
                    break;
                case NotificationKind.ApplicationRejected:
                    buttons.Add(new ChatButton("Заполнить заново", () => Start(chat, chat.InviteCode)));
                    break;
                case NotificationKind.HomeworkPublished or NotificationKind.HomeworkChanged when n.HomeworkId is { } hw:
                    buttons.Add(new ChatButton("Сдать", () => Guard(chat, () => AskPdf(chat, hw))));
                    break;
            }
            chat.Messages.Add(new ChatMessage(true, n.Text, buttons, n.HasFile ? n.Id : null, n.FileName));
            await api.Ack(n.Id);
        }
        if (batch.Length > 0)
            Changed?.Invoke();
        return batch.Length;
    }

    public Task<byte[]> DownloadFile(int notificationId) => api.NotificationFile(notificationId);

    // ── Анкета ────────────────────────────────────────────────────────

    private async Task BeginApplication(StudentChat chat, BotStudentDto student, string? code, string subject, string teacher, BotCourseOption[] courses)
    {
        var courseIds = courses.Select(c => c.CourseId).ToHashSet();
        var existing = student.Enrollments.FirstOrDefault(e => courseIds.Contains(e.CourseId) && e.Status is EnrollmentStatus.Pending or EnrollmentStatus.Approved);
        if (existing?.Status == EnrollmentStatus.Pending)
        {
            Bot(chat, "Заявка уже на рассмотрении");
            return;
        }
        if (existing?.Status == EnrollmentStatus.Approved)
        {
            Bot(chat, "Вы уже записаны", [MyTasksButton(chat)]);
            return;
        }
        if (courses.Length == 0)
        {
            Bot(chat, "Набор закрыт, обратитесь к преподавателю");
            return;
        }

        chat.InviteCode = code;
        chat.SubjectName = subject;
        chat.Courses = courses;
        chat.Known = student.Known;
        Bot(chat, $"Запись на предмет «{subject}», преподаватель {teacher}.");

        // С-1.9: известный студент подтверждает ФИО кнопкой и отвечает только про подгруппу
        if (student.Known)
        {
            chat.LastName = student.LastName;
            chat.FirstName = student.FirstName;
            chat.Step = Step.ConfirmName;
            Bot(chat, $"Это вы: {student.LastName} {student.FirstName}?",
            [
                new ChatButton("Да, это я", () => Guard(chat, () => { User(chat, "Да, это я"); AskSubgroup(chat); return Task.CompletedTask; })),
                new ChatButton("Исправить", () => Guard(chat, () => { User(chat, "Исправить"); AskLastName(chat); return Task.CompletedTask; })),
            ]);
            return;
        }
        AskLastName(chat);
    }

    private void AskLastName(StudentChat chat)
    {
        chat.Step = Step.LastName;
        Bot(chat, "Ваша фамилия?");
    }

    private void AskSubgroup(StudentChat chat)
    {
        chat.Step = Step.Subgroup;
        Bot(chat, "Выберите подгруппу:", chat.Courses.Select(c => new ChatButton(c.Name, () => Guard(chat, () =>
        {
            User(chat, c.Name);
            chat.CourseId = c.CourseId;
            if (chat.Known)
                AskSummary(chat);
            else
            {
                chat.Step = Step.Consent;
                Bot(chat, "Согласие на обработку персональных данных: я даю согласие на обработку моих фамилии, имени, учебной группы и Telegram-аккаунта для организации сдачи и проверки домашних заданий.",
                    [new ChatButton("Согласен", () => Guard(chat, () => { User(chat, "Согласен"); AskSummary(chat); return Task.CompletedTask; }))]);
            }
            return Task.CompletedTask;
        }))).ToArray());
    }

    private void AskSummary(StudentChat chat)
    {
        chat.Step = Step.Summary;
        var course = chat.Courses.First(c => c.CourseId == chat.CourseId);
        Bot(chat, $"Проверьте: {chat.LastName} {chat.FirstName}, {course.Name}, {chat.SubjectName}",
        [
            new ChatButton("Отправить", () => Guard(chat, async () =>
            {
                User(chat, "Отправить");
                try
                {
                    var r = await api.Apply(new BotApplicationRequest(chat.TelegramUserId, chat.Username, chat.InviteCode, chat.CourseId!.Value,
                        chat.FirstName ?? "", chat.LastName ?? "", true));
                    Bot(chat, r.Message);
                    chat.Step = Step.Idle;
                    UpdateTitle(chat, await api.Student(chat.TelegramUserId));
                }
                catch (ApiError e) when (e.Code == ErrorCodes.InvalidName)
                {
                    Bot(chat, e.Message);
                    AskLastName(chat);
                }
            })),
            new ChatButton("Заполнить заново", () => Guard(chat, () => { User(chat, "Заполнить заново"); chat.Known = false; AskLastName(chat); return Task.CompletedTask; })),
        ]);
    }

    // ── Задания и сдача ───────────────────────────────────────────────

    private ChatButton MyTasksButton(StudentChat chat) => new("Мои задания", () => Guard(chat, () => { User(chat, "Мои задания"); return ShowTasks(chat); }));

    private async Task ShowTasks(StudentChat chat)
    {
        var tasks = await api.Homeworks(chat.TelegramUserId);
        if (tasks.Active.Length == 0 && tasks.Finished.Length == 0)
        {
            Bot(chat, "Заданий пока нет");
            return;
        }

        string Line(BotHomeworkListItem t) =>
            $"{t.Title} — до {Fmt.Dt(t.Deadline)} — {(t.Status == BotHomeworkStatus.Reviewed ? $"проверено: {Fmt.Score(t.Score)}" : Fmt.Status(t.Status))}";

        var text = string.Join("\n", tasks.Active.GroupBy(t => t.SubjectName).Select(g => $"{g.Key}:\n" + string.Join("\n", g.Select(t => "  • " + Line(t)))));
        if (tasks.Finished.Length > 0)
            text += (text.Length > 0 ? "\n\n" : "") + "Завершённые:\n" + string.Join("\n", tasks.Finished.Select(t => $"  • {t.SubjectName}: {Line(t)}"));
        Bot(chat, text, tasks.Active.Concat(tasks.Finished).Select(t => new ChatButton(t.Title, () => Guard(chat, async () =>
        {
            User(chat, t.Title);
            await ShowCard(chat, t.Id);
        }))).ToArray());
    }

    private async Task ShowCard(StudentChat chat, int homeworkId)
    {
        var hw = await api.Homework(chat.TelegramUserId, homeworkId);
        var lines = new List<string> { $"{hw.SubjectName} — {hw.Title}", $"Условие: {hw.Link}", $"Дедлайн: {Fmt.Dt(hw.Deadline)}" };
        if (hw.Versions.Length > 0)
            lines.Add("Версии: " + string.Join("; ", hw.Versions.Select(v => $"v{v.Version} {Fmt.Dt(v.SubmittedAt)}{(v.IsLate ? " (с опозданием)" : "")}")));
        if (hw.LastResult is { } r)
        {
            lines.Add($"Проверка v{r.Version}: {(r.Total is { } t ? Fmt.Score(t) : "без оценки")}");
            lines.AddRange(r.Subtasks.Where(s => s.Score is not null).Select(s => $"  {s.Title}: {Fmt.Score(s.Score)}{(s.MaxScore is { } m ? $" из {Fmt.Score(m)}" : "")}"));
            if (!string.IsNullOrWhiteSpace(r.Comment))
                lines.Add($"Комментарий: {r.Comment}");
            if (r.ReviewedBy is not null)
                lines.Add($"Проверил(а): {r.ReviewedBy}");
        }
        if (!hw.CanSubmit)
            lines.Add("Курс завершён, сдача закрыта");
        Bot(chat, string.Join("\n", lines), hw.CanSubmit
            ? [new ChatButton(hw.Versions.Length > 0 ? "Пересдать" : "Сдать", () => Guard(chat, () => AskPdf(chat, hw.Id)))]
            : []);
    }

    private Task AskPdf(StudentChat chat, int homeworkId)
    {
        chat.Step = Step.WaitingPdf;
        chat.AwaitingPdfFor = homeworkId;
        Bot(chat, "Пришлите один PDF-файл с решением", [new ChatButton("Отмена", () => Guard(chat, () =>
        {
            User(chat, "Отмена");
            chat.Step = Step.Idle;
            chat.AwaitingPdfFor = null;
            Bot(chat, "Отменено");
            return Task.CompletedTask;
        }))]);
        return Task.CompletedTask;
    }

    private async Task Submit(StudentChat chat, int homeworkId, byte[] bytes, string name)
    {
        var r = await api.Submit(chat.TelegramUserId, homeworkId, bytes, name);
        chat.Step = Step.Idle;
        chat.AwaitingPdfFor = null;
        Bot(chat, r.Message, [MyTasksButton(chat)]);
    }

    // ── Служебное ─────────────────────────────────────────────────────

    private StudentChat NewStudentSilently(long tgId)
    {
        var chat = new StudentChat(tgId, $"student{tgId % 10000}");
        Chats.Add(chat);
        return chat;
    }

    private static void Reset(StudentChat chat)
    {
        chat.Step = Step.Idle;
        chat.CourseId = null;
        chat.AwaitingPdfFor = null;
        chat.PendingPdf = null;
    }

    private static void UpdateTitle(StudentChat chat, BotStudentDto student)
    {
        if (student.Known)
            chat.Title = $"{student.LastName} {student.FirstName}";
    }

    private void User(StudentChat chat, string text)
    {
        chat.Messages.Add(new ChatMessage(false, text, []));
        Changed?.Invoke();
    }

    private void Bot(StudentChat chat, string text, IReadOnlyList<ChatButton>? buttons = null)
    {
        chat.Messages.Add(new ChatMessage(true, text, buttons ?? []));
        Changed?.Invoke();
    }

    /// <summary>Ошибки API показываем как ответ бота — текст из ProblemDetails уже человеческий.</summary>
    private async Task Guard(StudentChat chat, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ApiError e)
        {
            Bot(chat, e.Message);
        }
        catch (HttpRequestException e)
        {
            Bot(chat, $"Сервер недоступен: {e.Message}");
        }
    }
}
