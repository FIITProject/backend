using Fiit.Contracts;

namespace Fiit.Api.Domain;

public static class Grading
{
    /// <summary>Итог проверки: вписанный вручную, иначе сумма баллов по подзадачам (если хоть один балл есть).</summary>
    public static decimal? Total(decimal? totalOverride, IEnumerable<decimal> subtaskScores)
    {
        if (totalOverride is not null)
            return totalOverride;
        var scores = subtaskScores.ToList();
        return scores.Count == 0 ? null : scores.Sum();
    }

    public static bool IsLate(DateTimeOffset submittedAt, DateTimeOffset deadline) => submittedAt > deadline;

    /// <summary>Статус ячейки «студент × ДЗ» по последней версии (ТЗ 1.4).</summary>
    public static CellStatus Cell(bool assigned, Submission? latest, DateTimeOffset deadline, DateTimeOffset now)
    {
        if (!assigned)
            return CellStatus.NotAssigned;
        if (latest is null)
            return now > deadline ? CellStatus.Overdue : CellStatus.NotSubmitted;
        if (latest.Review is not { IsSent: true } review)
            return CellStatus.OnReview;
        return review.HasUnsentChanges ? CellStatus.ReviewedWithUnsentChanges : CellStatus.Reviewed;
    }

    public static BotHomeworkStatus ForStudent(CellStatus status) => status switch
    {
        CellStatus.Overdue => BotHomeworkStatus.Overdue,
        CellStatus.OnReview => BotHomeworkStatus.OnReview,
        CellStatus.Reviewed or CellStatus.ReviewedWithUnsentChanges => BotHomeworkStatus.Reviewed,
        _ => BotHomeworkStatus.NotSubmitted,
    };

    /// <summary>Итоговый столбец (П-8.3): среднее по ДЗ с отправленной проверкой, у которой есть оценка.</summary>
    public static decimal? Average(IEnumerable<decimal?> sentScores)
    {
        var scores = sentScores.OfType<decimal>().ToList();
        return scores.Count == 0 ? null : Math.Round(scores.Average(), 2);
    }
}
