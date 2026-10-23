namespace Fiit.Contracts;

public record SubjectDto(int Id, string Name);

public record CreateSubjectRequest(string Name);

public record CourseDto(
    int Id,
    int SubjectId,
    string SubjectName,
    string GroupName,
    string SubgroupName,
    SemesterDto Semester,
    int AdminTeacherId,
    string AdminTeacherName,
    bool IsAdmin,
    bool IsArchived,
    int ApprovedCount,
    int PendingCount);

/// <summary>Group — например «ФТ-201» или «фт 201-1» (тогда подгруппа берётся из строки). Semester = null — текущий.</summary>
public record CreateCourseRequest(int SubjectId, string Group, int? Subgroup, SemesterDto? Semester);

public record CourseIdsRequest(int[] CourseIds);

public record CourseAccessDto(int TeacherId, string FullName, string Login);

public enum InviteStatus
{
    Active,
    Expired,
    Closed,
}

public record InviteDto(
    int Id,
    string Code,
    string Link,
    int SubjectId,
    string SubjectName,
    SemesterDto Semester,
    CourseRef[] Courses,
    DateTimeOffset? ExpiresAt,
    InviteStatus Status,
    int ApplicationsCount,
    string AuthorName,
    DateTimeOffset CreatedAt);

public record CreateInviteRequest(int SubjectId, int[] CourseIds, DateTimeOffset? ExpiresAt);

/// <summary>Close = true закрывает приглашение досрочно; иначе меняется срок (null — бессрочно).</summary>
public record UpdateInviteRequest(DateTimeOffset? ExpiresAt, bool Close);

public enum EnrollmentStatus
{
    Pending,
    Approved,
    Rejected,
    Removed,
}

public record EnrollmentDto(
    int Id,
    int StudentId,
    string LastName,
    string FirstName,
    int CourseId,
    string SubjectName,
    string CourseName,
    string? TelegramUsername,
    EnrollmentStatus Status,
    DateTimeOffset CreatedAt,
    string? RejectReason,
    bool BotBlocked,
    TagRef[] Tags);

public record RejectRequest(int[] Ids, string? Reason);

public record TagDto(int Id, string Name, int StudentsCount);

public record TagNameRequest(string Name);

public record TagStudentsRequest(int[] StudentIds);
