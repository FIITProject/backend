namespace Fiit.Api.Domain;

public class Homework
{
    public int Id { get; set; }
    public int SubjectId { get; set; }
    public int SemesterYear { get; set; }
    public SemesterTerm SemesterTerm { get; set; }
    public int AuthorId { get; set; }
    public required string Title { get; set; }
    public required string Link { get; set; }
    public DateTimeOffset Deadline { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    /// <summary>Курсы ДЗ. Препод видит ДЗ, только если имеет доступ хотя бы к одному из них.</summary>
    public List<Course> Courses { get; set; } = [];
    public List<Subtask> Subtasks { get; set; } = [];
    public List<HomeworkTarget> Targets { get; set; } = [];
}

public enum HomeworkTargetKind
{
    Student,
    Tag,
    Course,
}

/// <summary>Сужение адресатов внутри курсов ДЗ. Нет правил — ДЗ выдано всем студентам курсов. Видимость в боте считается динамически.</summary>
public class HomeworkTarget
{
    public int Id { get; set; }
    public int HomeworkId { get; set; }
    public HomeworkTargetKind Kind { get; set; }
    public int RefId { get; set; }
}

public class Subtask
{
    public int Id { get; set; }
    public int HomeworkId { get; set; }
    public int Order { get; set; }
    public required string Title { get; set; }
    public decimal? MaxScore { get; set; }
}
