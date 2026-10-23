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

/// <summary>Score — оценка из отправленной студенту проверки последней версии; ReviewedBy — кто её отправил.</summary>
public record GradebookCell(int HomeworkId, CellStatus Status, decimal? Score, bool IsLate, int Versions, int? LatestSubmissionId, string? ReviewedBy = null);

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

/// <summary>
/// Annotations — JSON-массив пометок в координатах страницы PDF (пункты, начало — левый верхний угол).
/// UpdatedByName — кто последним правил черновик, SentByName — кто отправил студенту (подпись).
/// </summary>
public record ReviewDto(
    string Annotations,
    string? Comment,
    SubtaskScoreDto[] SubtaskScores,
    decimal? TotalOverride,
    decimal? Total,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SentAt,
    bool HasUnsentChanges,
    string UpdatedByName,
    string? SentByName);

public record SubtaskScoreDto(int SubtaskId, decimal? Score);

/// <summary>BaseUpdatedAt — UpdatedAt черновика, с которого начинали править; если черновик успел поменять коллега — 409.</summary>
public record SaveReviewRequest(string Annotations, string? Comment, SubtaskScoreDto[] SubtaskScores, decimal? TotalOverride, DateTimeOffset? BaseUpdatedAt = null);
