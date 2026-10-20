namespace Fiit.Contracts;

public enum HomeworkTargetKind
{
    Student,
    Tag,
    Course,
}

public record HomeworkTargetDto(HomeworkTargetKind Kind, int RefId);

public record SubtaskDto(int? Id, string Title, decimal? MaxScore);

/// <summary>
/// Создание/изменение ДЗ. Targets пустой — ДЗ выдано всем студентам курсов.
/// NotifyStudents — после публикации разослать уведомление об изменении дедлайна/подзадач.
/// </summary>
public record HomeworkEditRequest(
    int SubjectId,
    SemesterDto Semester,
    string Title,
    string Link,
    DateTimeOffset Deadline,
    SubtaskDto[] Subtasks,
    int[] CourseIds,
    HomeworkTargetDto[] Targets,
    bool NotifyStudents = true);

public record HomeworkDto(
    int Id,
    int SubjectId,
    string SubjectName,
    SemesterDto Semester,
    string Title,
    string Link,
    DateTimeOffset Deadline,
    bool IsPublished,
    DateTimeOffset? PublishedAt,
    string AuthorName,
    SubtaskDto[] Subtasks,
    CourseRef[] Courses,
    HomeworkTargetDto[] Targets,
    int RecipientsCount,
    int SubmittedCount,
    int ReviewedCount,
    bool SubtasksLocked,
    bool IsReadOnly);

public record RecipientsPreviewRequest(int[] CourseIds, HomeworkTargetDto[] Targets);

public record RecipientsPreviewResponse(int Count);

public record PublishResponse(int Notified);
