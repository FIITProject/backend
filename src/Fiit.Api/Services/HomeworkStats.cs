using Fiit.Api.Data;
using Fiit.Api.Domain;
using Fiit.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Services;

public record HomeworkCounts(int Recipients, int Submitted, int Reviewed);

/// <summary>Счётчики «сдано X из Y, проверено Z из X» (П-7.1) по текущим адресатам и последним версиям.</summary>
public class HomeworkStats(AppDbContext db, StudentFactsLoader facts)
{
    public async Task<Dictionary<int, HomeworkCounts>> CountsAsync(IReadOnlyList<Homework> homeworks, CancellationToken ct)
    {
        var courseIds = homeworks.SelectMany(h => h.Courses).Select(c => c.Id).Distinct().ToList();
        var allFacts = await facts.ForCoursesAsync(courseIds, ct);
        var hwIds = homeworks.Select(h => h.Id).ToList();
        var latest = await LatestAsync(hwIds, ct);

        return homeworks.ToDictionary(h => h.Id, h =>
        {
            var recipients = StudentFactsLoader.Recipients(h, allFacts);
            var submitted = 0;
            var reviewed = 0;
            foreach (var s in recipients)
            {
                if (!latest.TryGetValue((h.Id, s), out var sub))
                    continue;
                submitted++;
                if (sub.Review is { IsSent: true })
                    reviewed++;
            }
            return new HomeworkCounts(recipients.Count, submitted, reviewed);
        });
    }

    /// <summary>Последняя версия по каждой паре (ДЗ, студент) вместе с проверкой.</summary>
    public async Task<Dictionary<(int HomeworkId, int StudentId), (Submission Latest, int Versions)>> LatestWithCountAsync(IReadOnlyCollection<int> homeworkIds, CancellationToken ct)
    {
        var all = await db.Submissions.AsNoTracking()
            .Include(s => s.Review)
            .Where(s => homeworkIds.Contains(s.HomeworkId))
            .ToListAsync(ct);
        return all.GroupBy(s => (s.HomeworkId, s.StudentId))
            .ToDictionary(g => g.Key, g => (g.MaxBy(s => s.Version)!, g.Count()));
    }

    private async Task<Dictionary<(int, int), Submission>> LatestAsync(IReadOnlyCollection<int> homeworkIds, CancellationToken ct) =>
        (await LatestWithCountAsync(homeworkIds, ct)).ToDictionary(kv => kv.Key, kv => kv.Value.Latest);

    public Task<bool> SubtasksLockedAsync(int homeworkId, CancellationToken ct) =>
        db.SubtaskScores.AnyAsync(s => db.Reviews.Any(r => r.Id == s.ReviewId && db.Submissions.Any(x => x.Id == r.SubmissionId && x.HomeworkId == homeworkId)), ct);
}
