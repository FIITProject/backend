using System.Net;
using Fiit.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fiit.Bot.Core;

/// <summary>
/// Диалог студента с ботом — сценарии раздела 4 ТЗ поверх /api/bot/*. Не знает про Telegram:
/// настоящий бот и эмулятор на сайте подают сюда события и получают сообщения через <see cref="IChatOutput"/>.
/// В личном чате id чата совпадает с id пользователя Telegram.
/// </summary>
public class Dialog(IBotApi api, IChatOutput output, IChatStateStore states, ILogger<Dialog>? log = null)
{
    public const string MyTasks = "Мои задания";
    public const long MaxPdfBytes = 20 * 1024 * 1024;
    private const int MaxListButtons = 40;

    private const string ConsentText =
        "Согласие на обработку персональных данных: я даю согласие на обработку моих фамилии, имени, учебной группы и Telegram-аккаунта " +
        "для организации сдачи и проверки домашних заданий.";

    private const string HelpText =
        "/tasks — мои задания\n/start — приветствие или запись по ссылке-приглашению\n/help — справка\n" +
        "Чтобы сдать ДЗ, откройте его в «Мои задания» и нажмите «Сдать», затем пришлите один PDF-файл.";

    private readonly ILogger _log = log ?? NullLogger<Dialog>.Instance;

    // ── События ───────────────────────────────────────────────────────

    /// <summary>/start [код приглашения] — С-1.1, С-1.1а.</summary>
    public Task StartAsync(BotUser user, string? payload, CancellationToken ct) => GuardAsync(user, async () =>
    {
        var state = states.Get(user.Id);
        state.Reset();
        var student = await api.StudentAsync(user.Id, ct);
        var code = payload?.Trim();

        if (!string.IsNullOrEmpty(code))
        {
            BotInviteDto invite;
            try
            {
                invite = await api.InviteAsync(code, ct);
            }
            catch (BotApiException e) when (e.Status == HttpStatusCode.NotFound)
            {
                await SayAsync(user, "Приглашение не найдено, попросите у преподавателя ссылку", ct);
                return;
            }
            if (!invite.IsOpen)
            {
                await SayAsync(user, "Набор закрыт, обратитесь к преподавателю", ct);
                return;
            }
            await BeginApplicationAsync(user, state, student, invite.Code, invite.SubjectName, invite.TeacherName, invite.Courses, ct);
            return;
        }

        if (student.HasApprovedCourse)
        {
            await SendAsync(user, new BotMessage($"Здравствуйте, {student.FirstName}! /tasks — мои задания, /help — справка.", [], ShowMenu: true), ct);
            return;
        }

        var recruiting = await api.RecruitingAsync(ct);
        if (recruiting.Length == 0)
        {
            await SayAsync(user, "Сейчас нет открытых наборов, попросите у преподавателя ссылку", ct);
            return;
        }
        await SendAsync(user, BotMessage.Column("Выберите предмет:",
            recruiting.Select(r => new Button($"{r.SubjectName} — {r.TeacherName}", Callbacks.Recruiting(r.SubjectId, r.TeacherId)))), ct);
    });

    /// <summary>Текст от студента: ответ на шаг анкеты или команда.</summary>
    public Task TextAsync(BotUser user, string text, CancellationToken ct)
    {
        text = text.Trim();
        if (text.Length == 0)
            return Task.CompletedTask;
        if (text == "/start" || text.StartsWith("/start "))
            return StartAsync(user, text.Length > 6 ? text[6..] : null, ct);

        return GuardAsync(user, async () =>
        {
            var state = states.Get(user.Id);
            var isCommand = text.StartsWith('/') || text == MyTasks;
            if (!isCommand && state.Step is Step.LastName or Step.FirstName)
            {
                var name = PersonName.Normalize(text);
                if (name is null)
                {
                    await SayAsync(user, "Только буквы, пробел и дефис, до 100 символов. " + (state.Step == Step.LastName ? "Ваша фамилия?" : "Ваше имя?"), ct);
                    return;
                }
                if (state.Step == Step.LastName)
                {
                    state.LastName = name;
                    state.Step = Step.FirstName;
                    await SayAsync(user, "Ваше имя?", ct);
                }
                else
                {
                    state.FirstName = name;
                    await AskSubgroupAsync(user, state, ct);
                }
                return;
            }

            var student = await api.StudentAsync(user.Id, ct);
            if (!student.HasApprovedCourse)
            {
                await SayAsync(user, NotApprovedText(student), ct);
                return;
            }

            switch (text.Split('@')[0])
            {
                case "/tasks" or MyTasks:
                    await ShowTasksAsync(user, ct);
                    return;
                case "/help":
                    await SendAsync(user, new BotMessage(HelpText, [], ShowMenu: true), ct);
                    return;
                default:
                    await SendAsync(user, state.Step == Step.WaitingPdf
                        ? BotMessage.Row("Пришлите один PDF-файл с решением или нажмите «Отмена»", new Button("Отмена", Callbacks.Cancel))
                        : BotMessage.Row("Не понял. /tasks — мои задания, /help — справка", TasksButton), ct);
                    return;
            }
        });
    }

    /// <summary>Нажатие кнопки под сообщением.</summary>
    public Task CallbackAsync(BotUser user, string data, CancellationToken ct) => GuardAsync(user, async () =>
    {
        var state = states.Get(user.Id);
        var parts = data.Split(':');
        switch (parts)
        {
            case [Callbacks.TasksKey]:
                await RequireApprovedAsync(user, ct, () => ShowTasksAsync(user, ct));
                return;
            case [Callbacks.HomeworkKey, var id] when int.TryParse(id, out var hwId):
                await RequireApprovedAsync(user, ct, () => ShowCardAsync(user, hwId, ct));
                return;
            case [Callbacks.SubmitKey, var id] when int.TryParse(id, out var hwId):
                await RequireApprovedAsync(user, ct, () => AskPdfAsync(user, state, hwId, ct));
                return;
            case [Callbacks.AttachKey, var id] when int.TryParse(id, out var hwId):
                if (state.PendingPdf is not { } pdf)
                {
                    await SayAsync(user, "Файл уже не найден, пришлите его ещё раз", ct);
                    return;
                }
                state.PendingPdf = null;
                await SubmitAsync(user, state, hwId, pdf, ct);
                return;
            case [Callbacks.CancelKey]:
                state.Reset();
                await SayAsync(user, "Отменено", ct);
                return;
            case [Callbacks.StartKey]:
                await StartAsync(user, null, ct);
                return;
            case [Callbacks.RecruitingKey, var s, var t] when int.TryParse(s, out var subjectId) && int.TryParse(t, out var teacherId):
                var recruiting = (await api.RecruitingAsync(ct)).FirstOrDefault(r => r.SubjectId == subjectId && r.TeacherId == teacherId);
                if (recruiting is null)
                {
                    await SayAsync(user, "Набор закрыт, обратитесь к преподавателю", ct);
                    return;
                }
                state.Reset();
                await BeginApplicationAsync(user, state, await api.StudentAsync(user.Id, ct), null,
                    recruiting.SubjectName, recruiting.TeacherName, recruiting.Courses, ct);
                return;
        }

        // Дальше — шаги анкеты: после перезапуска бота состояние пустое, просим начать заново
        if (state.Courses.Length == 0)
        {
            await SendAsync(user, BotMessage.Row("Анкета устарела, начните заново", new Button("Начать заново", Callbacks.Start)), ct);
            return;
        }
        switch (parts)
        {
            case [Callbacks.NameYesKey] when state.Step == Step.ConfirmName:
                await AskSubgroupAsync(user, state, ct);
                return;
            case [Callbacks.NameFixKey] when state.Step == Step.ConfirmName:
                await AskLastNameAsync(user, state, ct);
                return;
            case [Callbacks.SubgroupKey, var c] when state.Step == Step.Subgroup && int.TryParse(c, out var courseId) && state.Courses.Any(x => x.CourseId == courseId):
                state.CourseId = courseId;
                if (state.Known)
                    await AskSummaryAsync(user, state, ct);
                else
                {
                    state.Step = Step.Consent;
                    await SendAsync(user, BotMessage.Row(ConsentText, new Button("Согласен", Callbacks.Consent)), ct);
                }
                return;
            case [Callbacks.ConsentKey] when state.Step == Step.Consent:
                await AskSummaryAsync(user, state, ct);
                return;
            case [Callbacks.ApplyKey] when state.Step == Step.Summary:
                await ApplyAsync(user, state, ct);
                return;
            case [Callbacks.RefillKey] when state.Step == Step.Summary:
                state.Known = false;
                await AskLastNameAsync(user, state, ct);
                return;
            default:
                await SayAsync(user, "Эта кнопка уже не действует", ct);
                return;
        }
    });

    /// <summary>Студент прислал файл — С-5.1, С-5.2, С-5.5.</summary>
    public Task DocumentAsync(BotUser user, IncomingDocument document, CancellationToken ct) => GuardAsync(user, async () =>
    {
        var state = states.Get(user.Id);

        // С-5.2: несколько файлов разом — Telegram присылает их отдельными сообщениями с общим media group; берём первый
        if (document.MediaGroupId is { } group)
        {
            if (group == state.LastMediaGroupId)
            {
                if (!state.WarnedAboutGroup)
                {
                    state.WarnedAboutGroup = true;
                    await SayAsync(user, "Принимаю только первый файл, остальные пропущены", ct);
                }
                return;
            }
            state.LastMediaGroupId = group;
            state.WarnedAboutGroup = false;
        }

        // Размер и тип проверяем до скачивания; содержимое потом ещё раз проверит API
        if (document.Size > MaxPdfBytes)
        {
            await SayAsync(user, "Файл слишком большой, максимум 20 МБ", ct);
            return;
        }
        if (!LooksLikePdf(document))
        {
            await SayAsync(user, "Нужен файл в формате PDF", ct);
            return;
        }

        if (state.Step == Step.WaitingPdf && state.AwaitingPdfFor is { } hwId)
        {
            await SubmitAsync(user, state, hwId, document, ct);
            return;
        }

        var student = await api.StudentAsync(user.Id, ct);
        if (!student.HasApprovedCourse)
        {
            await SayAsync(user, NotApprovedText(student), ct);
            return;
        }
        var tasks = await api.HomeworksAsync(user.Id, ct);
        if (tasks.Active.Length == 0)
        {
            await SayAsync(user, "Нет открытых заданий для сдачи", ct);
            return;
        }
        state.PendingPdf = document;
        await SendAsync(user, BotMessage.Column("К какому заданию отнести файл?",
            tasks.Active.Take(MaxListButtons).Select(t => new Button($"{t.SubjectName}: {t.Title}", Callbacks.Attach(t.Id)))), ct);
    });

    /// <summary>Как показать уведомление из очереди (С-2, С-3, С-6): текст готовит API, кнопки — бот.</summary>
    public static BotMessage Notification(BotNotificationDto n)
    {
        Button[] buttons = n.Kind switch
        {
            NotificationKind.ApplicationApproved => [TasksButton],
            NotificationKind.ApplicationRejected => [new Button("Заполнить заново", Callbacks.Start)],
            NotificationKind.HomeworkPublished or NotificationKind.HomeworkChanged when n.HomeworkId is { } hw => [new Button("Сдать", Callbacks.Submit(hw))],
            NotificationKind.ReviewSent when n.HomeworkId is { } hw => [new Button("Открыть задание", Callbacks.Homework(hw))],
            _ => [],
        };
        return BotMessage.Row(n.Text, buttons) with { Attachment = n.HasFile ? new Attachment(n.Id, n.FileName ?? "проверка.pdf") : null };
    }

    // ── Анкета ────────────────────────────────────────────────────────

    private async Task BeginApplicationAsync(BotUser user, ChatState state, BotStudentDto student, string? code, string subject, string teacher,
        BotCourseOption[] courses, CancellationToken ct)
    {
        var courseIds = courses.Select(c => c.CourseId).ToHashSet();
        var existing = student.Enrollments.FirstOrDefault(e => courseIds.Contains(e.CourseId) && e.Status is EnrollmentStatus.Pending or EnrollmentStatus.Approved);
        if (existing?.Status == EnrollmentStatus.Pending)
        {
            await SayAsync(user, "Заявка уже на рассмотрении", ct);
            return;
        }
        if (existing?.Status == EnrollmentStatus.Approved)
        {
            await SendAsync(user, BotMessage.Row("Вы уже записаны", TasksButton), ct);
            return;
        }
        if (courses.Length == 0)
        {
            await SayAsync(user, "Набор закрыт, обратитесь к преподавателю", ct);
            return;
        }

        state.InviteCode = code;
        state.SubjectName = subject;
        state.Courses = courses;
        state.Known = student.Known;
        // «Иванов И. И.» уже заканчивается точкой
        await SayAsync(user, $"Запись на предмет «{subject}», преподаватель {teacher}" + (teacher.EndsWith('.') ? "" : "."), ct);

        // С-1.9: известный студент подтверждает ФИО кнопкой и отвечает только про подгруппу
        if (student.Known)
        {
            state.LastName = student.LastName;
            state.FirstName = student.FirstName;
            state.Step = Step.ConfirmName;
            await SendAsync(user, BotMessage.Row($"Это вы: {student.LastName} {student.FirstName}?",
                new Button("Да, это я", Callbacks.NameYes), new Button("Исправить", Callbacks.NameFix)), ct);
            return;
        }
        await AskLastNameAsync(user, state, ct);
    }

    private Task AskLastNameAsync(BotUser user, ChatState state, CancellationToken ct)
    {
        state.Step = Step.LastName;
        return SayAsync(user, "Ваша фамилия?", ct);
    }

    private Task AskSubgroupAsync(BotUser user, ChatState state, CancellationToken ct)
    {
        state.Step = Step.Subgroup;
        return SendAsync(user, BotMessage.Column("Выберите подгруппу:", state.Courses.Select(c => new Button(c.Name, Callbacks.Subgroup(c.CourseId)))), ct);
    }

    private Task AskSummaryAsync(BotUser user, ChatState state, CancellationToken ct)
    {
        state.Step = Step.Summary;
        var course = state.Courses.First(c => c.CourseId == state.CourseId);
        return SendAsync(user, BotMessage.Row($"Проверьте: {state.LastName} {state.FirstName}, {course.Name}, {state.SubjectName}",
            new Button("Отправить", Callbacks.Apply), new Button("Заполнить заново", Callbacks.Refill)), ct);
    }

    private async Task ApplyAsync(BotUser user, ChatState state, CancellationToken ct)
    {
        try
        {
            // Согласие (С-1.4) уже получено: либо кнопкой «Согласен», либо раньше — у известного студента (С-1.9)
            var response = await api.ApplyAsync(new BotApplicationRequest(user.Id, user.Username, state.InviteCode, state.CourseId!.Value,
                state.FirstName ?? "", state.LastName ?? "", PdConsent: true), ct);
            state.Reset();
            state.Courses = [];
            await SayAsync(user, response.Message, ct);
        }
        catch (BotApiException e) when (e.Code == ErrorCodes.InvalidName)
        {
            await SayAsync(user, e.Message, ct);
            await AskLastNameAsync(user, state, ct);
        }
    }

    // ── Задания и сдача ───────────────────────────────────────────────

    private static Button TasksButton => new(MyTasks, Callbacks.Tasks);

    private async Task ShowTasksAsync(BotUser user, CancellationToken ct)
    {
        var tasks = await api.HomeworksAsync(user.Id, ct);
        if (tasks.Active.Length == 0 && tasks.Finished.Length == 0)
        {
            await SayAsync(user, "Заданий пока нет", ct);
            return;
        }

        static string Line(BotHomeworkListItem t) =>
            $"{t.Title} — до {Format.Dt(t.Deadline)} — {(t.Status == BotHomeworkStatus.Reviewed ? $"проверено: {(t.Score is null ? "без оценки" : Format.Score(t.Score))}" : Format.Status(t.Status))}";

        var text = string.Join("\n\n", tasks.Active.GroupBy(t => t.SubjectName).Select(g => $"{g.Key}:\n" + string.Join("\n", g.Select(t => "• " + Line(t)))));
        if (tasks.Finished.Length > 0)
            text += (text.Length > 0 ? "\n\n" : "") + "Завершённые:\n" + string.Join("\n", tasks.Finished.Select(t => $"• {t.SubjectName}: {Line(t)}"));
        var buttons = tasks.Active.Concat(tasks.Finished).Take(MaxListButtons).Select(t => new Button(t.Title, Callbacks.Homework(t.Id)));
        await SendAsync(user, BotMessage.Column(text, buttons), ct);
    }

    private async Task ShowCardAsync(BotUser user, int homeworkId, CancellationToken ct)
    {
        var hw = await api.HomeworkAsync(user.Id, homeworkId, ct);
        var lines = new List<string> { $"{hw.SubjectName} — {hw.Title}", $"Условие: {hw.Link}", $"Дедлайн: {Format.Dt(hw.Deadline)}" };
        if (hw.Subtasks.Length > 0)
            lines.Add("Подзадачи: " + string.Join("; ", hw.Subtasks.Select(s => s.MaxScore is { } m ? $"{s.Title} ({Format.Score(m)})" : s.Title)));
        if (hw.Versions.Length > 0)
            lines.Add("Версии:\n" + string.Join("\n", hw.Versions.Select(v => $"  v{v.Version} — {Format.Dt(v.SubmittedAt)}{(v.IsLate ? " (с опозданием)" : "")}")));
        if (hw.LastResult is { } r)
        {
            lines.Add($"Проверка v{r.Version}: {(r.Total is { } t ? Format.Score(t) : "без оценки")}");
            lines.AddRange(r.Subtasks.Where(s => s.Score is not null).Select(s => $"  {s.Title}: {Format.Score(s.Score)}{(s.MaxScore is { } m ? $" из {Format.Score(m)}" : "")}"));
            if (!string.IsNullOrWhiteSpace(r.Comment))
                lines.Add($"Комментарий: {r.Comment}");
            if (r.ReviewedBy is not null)
                lines.Add($"Проверил(а): {r.ReviewedBy}");
        }
        if (!hw.CanSubmit)
            lines.Add("Курс завершён, сдача закрыта");
        var text = string.Join("\n", lines);
        await SendAsync(user, hw.CanSubmit
            ? BotMessage.Row(text, new Button(hw.Versions.Length > 0 ? "Пересдать" : "Сдать", Callbacks.Submit(hw.Id)))
            : BotMessage.Plain(text), ct);
    }

    private Task AskPdfAsync(BotUser user, ChatState state, int homeworkId, CancellationToken ct)
    {
        state.Step = Step.WaitingPdf;
        state.AwaitingPdfFor = homeworkId;
        return SendAsync(user, BotMessage.Row("Пришлите один PDF-файл с решением", new Button("Отмена", Callbacks.Cancel)), ct);
    }

    /// <summary>С-5.3. При ошибке (файл не прошёл проверку) остаёмся в ожидании PDF — студент может прислать исправленный.</summary>
    private async Task SubmitAsync(BotUser user, ChatState state, int homeworkId, IncomingDocument document, CancellationToken ct)
    {
        var bytes = await document.Download(ct);
        var name = string.IsNullOrWhiteSpace(document.FileName) ? "работа.pdf" : document.FileName;
        var response = await api.SubmitAsync(user.Id, homeworkId, bytes, name, ct);
        state.Reset();
        await SendAsync(user, BotMessage.Row(response.Message, TasksButton), ct);
    }

    // ── Служебное ─────────────────────────────────────────────────────

    private static bool LooksLikePdf(IncomingDocument d) =>
        string.Equals(d.MimeType, "application/pdf", StringComparison.OrdinalIgnoreCase)
        || d.FileName?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true;

    private static string NotApprovedText(BotStudentDto student) =>
        student.Enrollments.Any(e => e.Status == EnrollmentStatus.Pending) ? "Заявка на рассмотрении у преподавателя" : "Сначала заполните анкету: /start";

    private async Task RequireApprovedAsync(BotUser user, CancellationToken ct, Func<Task> action)
    {
        var student = await api.StudentAsync(user.Id, ct);
        if (student.HasApprovedCourse)
            await action();
        else
            await SayAsync(user, NotApprovedText(student), ct);
    }

    private Task SayAsync(BotUser user, string text, CancellationToken ct) => SendAsync(user, BotMessage.Plain(text), ct);

    private Task SendAsync(BotUser user, BotMessage message, CancellationToken ct) => output.SendAsync(user.Id, message, ct);

    /// <summary>Ошибки API показываем студенту как ответ бота — текст из ProblemDetails уже человеческий.</summary>
    private async Task GuardAsync(BotUser user, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (BotApiException e) when ((int)e.Status < 500 || e.Code is not null)
        {
            await output.SendAsync(user.Id, BotMessage.Plain(e.Message), CancellationToken.None);
        }
        catch (Exception e) when (e is BotApiException or HttpRequestException or TaskCanceledException)
        {
            _log.LogError(e, "Backend call failed for user {UserId}", user.Id);
            await output.SendAsync(user.Id, BotMessage.Plain("Сервис временно недоступен, попробуйте чуть позже"), CancellationToken.None);
        }
    }
}

/// <summary>Команды кнопок. Короткие — callback_data в Telegram ограничен 64 байтами.</summary>
public static class Callbacks
{
    public const string TasksKey = "tasks";
    public const string HomeworkKey = "hw";
    public const string SubmitKey = "submit";
    public const string AttachKey = "attach";
    public const string CancelKey = "cancel";
    public const string StartKey = "start";
    public const string RecruitingKey = "rec";
    public const string NameYesKey = "me";
    public const string NameFixKey = "fix";
    public const string SubgroupKey = "sub";
    public const string ConsentKey = "consent";
    public const string ApplyKey = "apply";
    public const string RefillKey = "refill";

    public const string Tasks = TasksKey;
    public const string Cancel = CancelKey;
    public const string Start = StartKey;
    public const string NameYes = NameYesKey;
    public const string NameFix = NameFixKey;
    public const string Consent = ConsentKey;
    public const string Apply = ApplyKey;
    public const string Refill = RefillKey;

    public static string Homework(int id) => $"{HomeworkKey}:{id}";
    public static string Submit(int id) => $"{SubmitKey}:{id}";
    public static string Attach(int id) => $"{AttachKey}:{id}";
    public static string Recruiting(int subjectId, int teacherId) => $"{RecruitingKey}:{subjectId}:{teacherId}";
    public static string Subgroup(int courseId) => $"{SubgroupKey}:{courseId}";
}
