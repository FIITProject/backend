using Fiit.Contracts;

namespace Fiit.Api.Domain;

/// <summary>Что известно о студенте для расчёта адресатов: в каких курсах подтверждён и какие метки на нём висят.</summary>
public record StudentFacts(int StudentId, IReadOnlySet<int> ApprovedCourseIds, IReadOnlySet<int> TagIds);

/// <summary>
/// Адресаты ДЗ: подтверждённые студенты курсов ДЗ; если есть правила — только те, кто подходит хотя бы под одно
/// (конкретный студент, метка, отдельный курс).
/// </summary>
public static class Recipients
{
    public static bool Matches(IReadOnlyCollection<int> homeworkCourseIds, IReadOnlyCollection<HomeworkTarget> targets, StudentFacts s)
    {
        if (!homeworkCourseIds.Any(s.ApprovedCourseIds.Contains))
            return false;
        if (targets.Count == 0)
            return true;
        return targets.Any(t => t.Kind switch
        {
            HomeworkTargetKind.Student => t.RefId == s.StudentId,
            HomeworkTargetKind.Tag => s.TagIds.Contains(t.RefId),
            HomeworkTargetKind.Course => homeworkCourseIds.Contains(t.RefId) && s.ApprovedCourseIds.Contains(t.RefId),
            _ => false,
        });
    }
}
