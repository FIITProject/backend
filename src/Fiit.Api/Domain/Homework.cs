using Fiit.Contracts;

namespace Fiit.Api.Domain;

/// <summary>ДЗ в одном или нескольких курсах одного предмета и семестра. Черновик, пока PublishedAt = null.</summary>
public class Homework
{
    public int Id { get; set; }
    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;
    public int SemesterYear { get; set; }
    public SemesterTerm SemesterTerm { get; set; }
    public int AuthorId { get; set; }
    public Teacher Author { get; set; } = null!;
    public required string Title { get; set; }
    public required string Link { get; set; }
    public DateTimeOffset Deadline { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    /// <summary>Курсы ДЗ. Препод видит ДЗ, только если имеет доступ хотя бы к одному из них.</summary>
    public List<Course> Courses { get; set; } = [];
    public List<Subtask> Subtasks { get; set; } = [];
    public List<HomeworkTarget> Targets { get; set; } = [];

    public bool IsPublished => PublishedAt is not null;
}

/// <summary>Сужение адресатов внутри курсов ДЗ. Нет правил — ДЗ выдано всем студентам курсов. Видимость считается динамически.</summary>
public class HomeworkTarget
{
    public int Id { get; set; }
    public int HomeworkId { get; set; }
    public HomeworkTargetKind Kind { get; set; }
    public int RefId { get; set; }
}

/// <summary>Снапшот адресатов, которым ушло уведомление о ДЗ (П-7.5): кто попал в курс позже, уведомление не получает.</summary>
public class HomeworkRecipient
{
    public int HomeworkId { get; set; }
    public int StudentId { get; set; }
    public DateTimeOffset NotifiedAt { get; set; }
}

public class Subtask
{
    public int Id { get; set; }
    public int HomeworkId { get; set; }
    public int Order { get; set; }
    public required string Title { get; set; }
    public decimal? MaxScore { get; set; }
}
