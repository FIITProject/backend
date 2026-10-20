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
    public required string FileName { get; set; }
    public long FileSize { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public Review? Review { get; set; }
}

/// <summary>
/// Проверка конкретной версии. Annotations — JSON слоя пометок в координатах страниц PDF.
/// Черновик сохраняется автоматически; при отправке фиксируется снапшот того, что ушло студенту.
/// За проверкой не закреплён преподаватель: править и отправлять может любой с доступом к курсу,
/// подписывается тот, кто отправил (<see cref="SentById"/>).
/// </summary>
public class Review
{
    public int Id { get; set; }
    public int SubmissionId { get; set; }
    /// <summary>Кто последним правил черновик.</summary>
    public int UpdatedById { get; set; }
    public Teacher UpdatedBy { get; set; } = null!;
    public string Annotations { get; set; } = "[]";
    public string? Comment { get; set; }
    /// <summary>Итог, вписанный вручную. Если null — сумма <see cref="SubtaskScores"/>.</summary>
    public decimal? TotalOverride { get; set; }
    public List<SubtaskScore> SubtaskScores { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Хеш содержимого черновика; отличие от <see cref="SentHash"/> = «есть неотправленные правки».</summary>
    public string DraftHash { get; set; } = "";
    public DateTimeOffset? SentAt { get; set; }
    /// <summary>Кто отправил проверку студенту — его подпись видит студент.</summary>
    public int? SentById { get; set; }
    public Teacher? SentBy { get; set; }
    public string? SentHash { get; set; }
    public decimal? SentTotal { get; set; }
    public string? SentComment { get; set; }
    /// <summary>JSON <see cref="Fiit.Contracts.BotSubtaskResult"/>[] на момент отправки.</summary>
    public string? SentSubtasks { get; set; }
    public string? SentFileKey { get; set; }

    public bool IsSent => SentAt is not null;
    public bool HasUnsentChanges => IsSent && SentHash != DraftHash;
}

public class SubtaskScore
{
    public int ReviewId { get; set; }
    public int SubtaskId { get; set; }
    public decimal Score { get; set; }
}
