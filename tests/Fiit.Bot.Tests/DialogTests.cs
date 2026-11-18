using System.Net;
using Fiit.Bot.Core;
using Fiit.Contracts;

namespace Fiit.Bot.Tests;

/// <summary>Сценарии раздела 4 ТЗ — так же, как их пройдёт студент в Telegram.</summary>
public class DialogTests
{
    private static readonly BotUser User = new(42, "anna");
    private readonly FakeApi _api = new();
    private readonly RecordingOutput _out = new();
    private readonly Dialog _dialog;
    private readonly CancellationToken _ct = CancellationToken.None;

    public DialogTests() => _dialog = new Dialog(_api, _out, new InMemoryChatStateStore(TimeProvider.System));

    [Fact]
    public async Task New_student_fills_application_by_invite()
    {
        await _dialog.StartAsync(User, "good", _ct);
        Assert.Contains("Запись на предмет «Python», преподаватель Иванов И. И.", _out.Texts);
        Assert.Equal("Ваша фамилия?", _out.Last.Text);

        await _dialog.TextAsync(User, "Петрова", _ct);
        await _dialog.TextAsync(User, "Анна", _ct);
        Assert.Equal(["ФТ-201-1", "ФТ-201-2"], _out.Last.Rows.SelectMany(r => r).Select(b => b.Text));

        await _dialog.CallbackAsync(User, Callbacks.Subgroup(11), _ct);
        Assert.StartsWith("Согласие на обработку персональных данных", _out.Last.Text);
        await _dialog.CallbackAsync(User, Callbacks.Consent, _ct);
        Assert.Equal("Проверьте: Петрова Анна, ФТ-201-2, Python", _out.Last.Text);
        await _dialog.CallbackAsync(User, Callbacks.Apply, _ct);

        var application = Assert.Single(_api.Applications);
        Assert.Equal(new BotApplicationRequest(42, "anna", "good", 11, "Анна", "Петрова", true), application);
        Assert.StartsWith("Заявка отправлена преподавателю", _out.Last.Text);
    }

    [Fact]
    public async Task Invalid_name_is_asked_again()
    {
        await _dialog.StartAsync(User, "good", _ct);
        await _dialog.TextAsync(User, "Петрова123", _ct);
        Assert.Equal("Только буквы, пробел и дефис, до 100 символов. Ваша фамилия?", _out.Last.Text);
        await _dialog.TextAsync(User, "Петрова-Водкина", _ct);
        Assert.Equal("Ваше имя?", _out.Last.Text);
    }

    [Fact]
    public async Task Known_student_confirms_name_and_skips_consent()
    {
        _api.Student = FakeApi.Approved with { Enrollments = [] };
        await _dialog.StartAsync(User, "good", _ct);
        Assert.Equal("Это вы: Петрова Анна?", _out.Last.Text);

        await _dialog.CallbackAsync(User, Callbacks.NameYes, _ct);
        await _dialog.CallbackAsync(User, Callbacks.Subgroup(10), _ct);
        Assert.Equal("Проверьте: Петрова Анна, ФТ-201-1, Python", _out.Last.Text);
    }

    [Fact]
    public async Task Repeated_invite_while_pending()
    {
        _api.Student = new BotStudentDto(true, "Анна", "Петрова", true, false,
            [new BotEnrollmentDto(10, "Python", "ФТ-201-1", "Иванов И. И.", EnrollmentStatus.Pending, null)]);
        await _dialog.StartAsync(User, "good", _ct);
        Assert.Equal("Заявка уже на рассмотрении", _out.Last.Text);

        await _dialog.TextAsync(User, "/tasks", _ct);
        Assert.Equal("Заявка на рассмотрении у преподавателя", _out.Last.Text);
    }

    [Theory]
    [InlineData("bad", "Приглашение не найдено, попросите у преподавателя ссылку")]
    [InlineData("closed", "Набор закрыт, обратитесь к преподавателю")]
    public async Task Bad_or_closed_invite(string code, string expected)
    {
        _api.InviteOpen = false;
        await _dialog.StartAsync(User, code == "closed" ? "good" : code, _ct);
        Assert.Equal(expected, _out.Last.Text);
    }

    [Fact]
    public async Task Start_without_invite_shows_recruiting_and_then_subgroups()
    {
        await _dialog.TextAsync(User, "/start", _ct);
        var button = Assert.Single(_out.Last.Rows.SelectMany(r => r));
        Assert.Equal("Python — Иванов И. И.", button.Text);

        await _dialog.CallbackAsync(User, button.Data, _ct);
        await _dialog.TextAsync(User, "Петрова", _ct);
        await _dialog.TextAsync(User, "Анна", _ct);
        await _dialog.CallbackAsync(User, Callbacks.Subgroup(10), _ct);
        await _dialog.CallbackAsync(User, Callbacks.Consent, _ct);
        await _dialog.CallbackAsync(User, Callbacks.Apply, _ct);
        Assert.Null(Assert.Single(_api.Applications).InviteCode);
    }

    [Fact]
    public async Task No_recruiting()
    {
        _api.Recruiting = [];
        await _dialog.StartAsync(User, null, _ct);
        Assert.Equal("Сейчас нет открытых наборов, попросите у преподавателя ссылку", _out.Last.Text);
    }

    [Fact]
    public async Task Stale_application_button_after_restart()
    {
        await _dialog.CallbackAsync(User, Callbacks.Apply, _ct);
        Assert.Equal("Анкета устарела, начните заново", _out.Last.Text);
    }

    [Fact]
    public async Task Approved_student_submits_pdf_via_button()
    {
        _api.Student = FakeApi.Approved;
        await _dialog.TextAsync(User, Dialog.MyTasks, _ct);
        Assert.Contains("ДЗ 1 — до", _out.Last.Text);

        await _dialog.CallbackAsync(User, Callbacks.Submit(100), _ct);
        Assert.Equal("Пришлите один PDF-файл с решением", _out.Last.Text);
        await _dialog.DocumentAsync(User, Pdf("решение.pdf"), _ct);

        var (hwId, bytes, name) = Assert.Single(_api.Submissions);
        Assert.Equal((100, "решение.pdf"), (hwId, name));
        Assert.Equal("%PDF-"u8.ToArray(), bytes);
        Assert.StartsWith("Принято:", _out.Last.Text);
    }

    [Fact]
    public async Task Pdf_without_button_asks_which_homework()
    {
        _api.Student = FakeApi.Approved;
        await _dialog.DocumentAsync(User, Pdf("решение.pdf"), _ct);
        Assert.Equal("К какому заданию отнести файл?", _out.Last.Text);

        await _dialog.CallbackAsync(User, Callbacks.Attach(100), _ct);
        Assert.Single(_api.Submissions);
    }

    [Fact]
    public async Task Rejected_file_shows_api_message_and_keeps_waiting()
    {
        _api.Student = FakeApi.Approved;
        _api.SubmitError = new BotApiException(HttpStatusCode.BadRequest, "В файле есть активное содержимое", ErrorCodes.PdfUnsafe);
        await _dialog.CallbackAsync(User, Callbacks.Submit(100), _ct);
        await _dialog.DocumentAsync(User, Pdf("макрос.pdf"), _ct);
        Assert.Equal("В файле есть активное содержимое", _out.Last.Text);

        _api.SubmitError = null;
        await _dialog.DocumentAsync(User, Pdf("чистый.pdf"), _ct);
        Assert.Equal("чистый.pdf", Assert.Single(_api.Submissions).Name);
    }

    [Fact]
    public async Task Unavailable_backend_is_reported_politely()
    {
        _api.Student = FakeApi.Approved;
        _api.SubmitError = new BotApiException(HttpStatusCode.BadGateway, "Bad Gateway", null);
        await _dialog.CallbackAsync(User, Callbacks.Submit(100), _ct);
        await _dialog.DocumentAsync(User, Pdf("решение.pdf"), _ct);
        Assert.Equal("Сервис временно недоступен, попробуйте чуть позже", _out.Last.Text);
    }

    [Fact]
    public async Task Size_and_type_are_checked_before_download()
    {
        _api.Student = FakeApi.Approved;
        await _dialog.DocumentAsync(User, new IncomingDocument("big.pdf", Dialog.MaxPdfBytes + 1, "application/pdf", null, _ => throw new InvalidOperationException()), _ct);
        Assert.Equal("Файл слишком большой, максимум 20 МБ", _out.Last.Text);
        await _dialog.DocumentAsync(User, new IncomingDocument("фото.jpg", 100, "image/jpeg", null, _ => throw new InvalidOperationException()), _ct);
        Assert.Equal("Нужен файл в формате PDF", _out.Last.Text);
    }

    [Fact]
    public async Task Several_files_at_once_take_only_first()
    {
        _api.Student = FakeApi.Approved;
        await _dialog.CallbackAsync(User, Callbacks.Submit(100), _ct);
        await _dialog.DocumentAsync(User, Pdf("1.pdf", "album"), _ct);
        await _dialog.DocumentAsync(User, Pdf("2.pdf", "album"), _ct);
        await _dialog.DocumentAsync(User, Pdf("3.pdf", "album"), _ct);

        Assert.Equal("1.pdf", Assert.Single(_api.Submissions).Name);
        Assert.Single(_out.Texts, t => t == "Принимаю только первый файл, остальные пропущены");
    }

    [Fact]
    public async Task Not_registered_student_is_sent_to_application()
    {
        await _dialog.TextAsync(User, "/tasks", _ct);
        Assert.Equal("Сначала заполните анкету: /start", _out.Last.Text);
    }

    [Theory]
    [InlineData(NotificationKind.ApplicationApproved, "tasks")]
    [InlineData(NotificationKind.ApplicationRejected, "start")]
    [InlineData(NotificationKind.HomeworkPublished, "submit:5")]
    [InlineData(NotificationKind.HomeworkChanged, "submit:5")]
    [InlineData(NotificationKind.ReviewSent, "hw:5")]
    public void Notification_buttons(NotificationKind kind, string data)
    {
        var message = Dialog.Notification(new BotNotificationDto(1, 42, kind, "текст", 5, kind == NotificationKind.ReviewSent, "проверка.pdf", DateTimeOffset.UtcNow));
        Assert.Equal(data, Assert.Single(message.Rows.SelectMany(r => r)).Data);
        Assert.Equal(kind == NotificationKind.ReviewSent, message.Attachment is not null);
    }

    [Fact]
    public void Callback_data_fits_telegram_limit() =>
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(Callbacks.Recruiting(int.MaxValue, int.MaxValue)) <= 64);

    private static IncomingDocument Pdf(string name, string? group = null) =>
        new(name, 5, "application/pdf", group, _ => Task.FromResult("%PDF-"u8.ToArray()));
}
