using Fiit.Contracts;

namespace Fiit.Api.Domain;

/// <summary>
/// Курс — предмет у конкретной подгруппы в семестре, например «Тервер у ФИИТ-301-1, 2026 осень».
/// Админ — препод, который создал (ведёт практику). Архивный курс доступен только для чтения.
/// </summary>
public class Course
{
    public int Id { get; set; }
    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;
    public int SubgroupId { get; set; }
    public Subgroup Subgroup { get; set; } = null!;
    public int SemesterYear { get; set; }
    public SemesterTerm SemesterTerm { get; set; }
    public int AdminTeacherId { get; set; }
    public Teacher AdminTeacher { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public List<CourseAccess> Accesses { get; set; } = [];

    public bool IsArchived => ArchivedAt is not null;
    public SemesterDto Semester => new(SemesterYear, SemesterTerm);
}

/// <summary>Полный доступ к <see cref="Course"/>, выданный админом другому преподу.</summary>
public class CourseAccess
{
    public int CourseId { get; set; }
    public int TeacherId { get; set; }
    public Teacher Teacher { get; set; } = null!;
}
