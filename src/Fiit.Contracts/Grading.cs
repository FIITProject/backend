namespace Fiit.Contracts;

/// <summary>Статус ячейки «студент × ДЗ» (ТЗ 1.4), считается по последней версии.</summary>
public enum CellStatus
{
    NotAssigned,
    NotSubmitted,
    Overdue,
    OnReview,
    Reviewed,
    ReviewedWithUnsentChanges,
}

public record GradebookDto(GradebookHomework[] Homeworks, GradebookRow[] Rows);

public record GradebookHomework(int Id, string Title, DateTimeOffset Deadline, string Link, int AssignedCount, int SubmittedCount, int ReviewedCount);

public record GradebookRow(
    int StudentId,
    string LastName,
    string FirstName,
    string GroupName,
    string SubgroupName,
    int CourseId,
    GradebookCell[] Cells,
    decimal? Average);

/// <summary>Score — оценка из отправленной студенту проверки последней версии.</summary>
public record GradebookCell(int HomeworkId, CellStatus Status, decimal? Score, bool IsLate, int Versions, int? LatestSubmissionId);

public record ReviewContextDto(
    int HomeworkId,
    string HomeworkTitle,
    string HomeworkLink,
    DateTimeOffset Deadline,
    SubtaskDto[] Subtasks,
    int StudentId,
    string StudentName,
    string CourseName,
    bool IsReadOnly,
    SubmissionDto[] Submissions);

public record SubmissionDto(
    int Id,
    int Version,
    DateTimeOffset SubmittedAt,
    bool IsLate,
    TimeSpan? LateBy,
    string FileName,
    long FileSize,
    bool IsLatest,
    ReviewDto? Review);

/// <summary>Annotations — JSON-массив пометок в координатах страницы PDF (пункты, начало — левый верхний угол).</summary>
public record ReviewDto(
    string Annotations,
    string? Comment,
    SubtaskScoreDto[] SubtaskScores,
    decimal? TotalOverride,
    decimal? Total,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SentAt,
    bool HasUnsentChanges,
    string ReviewerName);

public record SubtaskScoreDto(int SubtaskId, decimal? Score);

public record SaveReviewRequest(string Annotations, string? Comment, SubtaskScoreDto[] SubtaskScores, decimal? TotalOverride);
