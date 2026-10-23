namespace Fiit.Contracts;

// Контракт между бэкендом и Telegram-ботом (заголовок X-Bot-Key). Бот — единственный, кто общается с Telegram.

public record BotCourseOption(int CourseId, string Name);

public record BotInviteDto(string Code, bool IsOpen, int SubjectId, string SubjectName, string TeacherName, SemesterDto Semester, BotCourseOption[] Courses);

/// <summary>Набор по предмету без ссылки (С-1.1а): «Предмет — преподаватель» и подгруппы из активных приглашений.</summary>
public record BotRecruitingDto(int SubjectId, string SubjectName, int TeacherId, string TeacherName, BotCourseOption[] Courses);

public record BotStudentDto(bool Known, string? FirstName, string? LastName, bool HasConsent, bool HasApprovedCourse, BotEnrollmentDto[] Enrollments);

public record BotEnrollmentDto(int CourseId, string SubjectName, string CourseName, string TeacherName, EnrollmentStatus Status, string? RejectReason);

/// <summary>Заполненная анкета. InviteCode = null — студент пришёл без ссылки и выбрал курс из открытых наборов.</summary>
public record BotApplicationRequest(
    long TelegramUserId,
    string? TelegramUsername,
    string? InviteCode,
    int CourseId,
    string FirstName,
    string LastName,
    bool PdConsent);

public record BotApplicationResponse(int EnrollmentId, string Message);

public enum BotHomeworkStatus
{
    NotSubmitted,
    Overdue,
    OnReview,
    Reviewed,
}

public record BotHomeworkListItem(int Id, string SubjectName, string Title, DateTimeOffset Deadline, BotHomeworkStatus Status, decimal? Score);

/// <summary>Active — ДЗ активных курсов, Finished — архивных («Завершённые»).</summary>
public record BotHomeworksDto(BotHomeworkListItem[] Active, BotHomeworkListItem[] Finished);

public record BotHomeworkDto(
    int Id,
    string SubjectName,
    string Title,
    string Link,
    DateTimeOffset Deadline,
    bool CanSubmit,
    SubtaskDto[] Subtasks,
    BotSubmissionDto[] Versions,
    BotReviewResultDto? LastResult);

public record BotSubmissionDto(int Version, DateTimeOffset SubmittedAt, bool IsLate);

/// <summary>ReviewedBy — преподаватель, который отправил проверку.</summary>
public record BotReviewResultDto(int Version, decimal? Total, BotSubtaskResult[] Subtasks, string? Comment, DateTimeOffset SentAt, string? ReviewedBy);

public record BotSubtaskResult(string Title, decimal? Score, decimal? MaxScore);

public record BotSubmitResponse(int Version, DateTimeOffset SubmittedAt, bool IsLate, string Message);

public enum NotificationKind
{
    ApplicationApproved,
    ApplicationRejected,
    HomeworkPublished,
    HomeworkChanged,
    ReviewSent,
}

/// <summary>Задача на отправку. Text — готовый текст сообщения; HomeworkId — для кнопки «Сдать»; HasFile — PDF качать через /api/bot/notifications/{id}/file.</summary>
public record BotNotificationDto(int Id, long TelegramUserId, NotificationKind Kind, string Text, int? HomeworkId, bool HasFile, string? FileName);

public record ClaimRequest(int Limit = 20);

/// <summary>Permanent — не ретраить; BotBlocked — студент заблокировал бота.</summary>
public record FailRequest(string Error, bool Permanent, bool BotBlocked);
