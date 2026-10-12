namespace Fiit.Api.Domain;

/// <summary>Версия сдачи ДЗ студентом (один PDF). Опоздание не хранится — сравнивается с текущим дедлайном ДЗ.</summary>
public class Submission
{
    public int Id { get; set; }
    public int HomeworkId { get; set; }
    public Homework Homework { get; set; } = null!;
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public int Version { get; set; }
    public required string FileKey { get; set; }
    public long FileSize { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public Review? Review { get; set; }
}

/// <summary>Проверка конкретной версии. Annotations — JSON слоя пометок в координатах страниц PDF.</summary>
public class Review
{
    public int Id { get; set; }
    public int SubmissionId { get; set; }
    public int TeacherId { get; set; }
    public string Annotations { get; set; } = "[]";
    public string? Comment { get; set; }
    /// <summary>Итог, вписанный вручную. Если null — сумма <see cref="SubtaskScores"/>.</summary>
    public decimal? TotalOverride { get; set; }
    public List<SubtaskScore> SubtaskScores { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? SentFileKey { get; set; }
}

public class SubtaskScore
{
    public int ReviewId { get; set; }
    public int SubtaskId { get; set; }
    public decimal Score { get; set; }
}
