using System.Text;
using Fiit.Api.Data;
using Fiit.Api.Domain;
using Fiit.Contracts;

namespace Fiit.Api.Services;

/// <summary>Кладёт уведомления студентам в outbox (сохраняет вызывающий, в той же транзакции). Тексты — из ТЗ, раздел 4.</summary>
public class Notifier(AppDbContext db, TimeProvider time)
{
    public void ApplicationApproved(int studentId, Course course) =>
        Add(studentId, NotificationKind.ApplicationApproved,
            $"Вас добавили: {course.Subject.Name}, {CourseName(course)}, преподаватель {Sentence(ShortName(course.AdminTeacher.FullName))} Теперь вам доступны задания");

    public void ApplicationRejected(int studentId, Course course, string? reason) =>
        Add(studentId, NotificationKind.ApplicationRejected,
            $"Заявка на {course.Subject.Name} отклонена" + (string.IsNullOrWhiteSpace(reason) ? "" : $"\nПричина: {reason}"));

    public void HomeworkPublished(int studentId, Homework hw) =>
        Add(studentId, NotificationKind.HomeworkPublished,
            $"Новое ДЗ — {hw.Subject.Name}: {hw.Title}\nДедлайн: {Display.Full(hw.Deadline)}\nУсловие: {hw.Link}",
            hw.Id);

    public void HomeworkChanged(int studentId, Homework hw, string changes) =>
        Add(studentId, NotificationKind.HomeworkChanged, $"ДЗ изменено — {hw.Subject.Name}: {hw.Title}\n{changes}", hw.Id);

    public void ReviewSent(int studentId, Homework hw, Submission submission, Review review, IReadOnlyList<BotSubtaskResult> subtasks, bool updated)
    {
        var text = new StringBuilder();
        if (updated)
            text.AppendLine("Проверка обновлена");
        text.AppendLine($"{hw.Subject.Name} — {hw.Title}, версия {submission.Version}");
        text.AppendLine($"Оценка: {(review.SentTotal is { } t ? Display.Score(t) : "без оценки")}");
        foreach (var s in subtasks.Where(s => s.Score is not null))
            text.AppendLine($"  {s.Title}: {Display.Score(s.Score!.Value)}{(s.MaxScore is { } m ? $" из {Display.Score(m)}" : "")}");
        if (!string.IsNullOrWhiteSpace(review.SentComment))
            text.AppendLine($"Комментарий: {review.SentComment}");
        Add(studentId, NotificationKind.ReviewSent, text.ToString().TrimEnd(), hw.Id, review.SentFileKey, $"{hw.Title} — проверка v{submission.Version}.pdf");
    }

    private void Add(int studentId, NotificationKind kind, string text, int? homeworkId = null, string? fileKey = null, string? fileName = null)
    {
        var now = time.GetUtcNow();
        db.Notifications.Add(new Notification
        {
            StudentId = studentId,
            Kind = kind,
            Text = text,
            HomeworkId = homeworkId,
            FileKey = fileKey,
            FileName = fileKey is null ? null : fileName,
            Status = NotificationStatus.Pending,
            CreatedAt = now,
            AvailableAt = now,
        });
    }

    /// <summary>Точка в конце, если её ещё нет («Иванов И. И.» уже заканчивается точкой).</summary>
    private static string Sentence(string s) => s.EndsWith('.') ? s : s + ".";

    public static string CourseName(Course c) => Subgroup.FullName(c.Subgroup.Group.Name, c.Subgroup.Number);

    /// <summary>«Иванов Иван Иванович» → «Иванов И. И.».</summary>
    public static string ShortName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 1 ? fullName : $"{parts[0]} {string.Join(' ', parts.Skip(1).Select(p => $"{p[0]}."))}";
    }
}
