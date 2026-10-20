using Fiit.Contracts;

namespace Fiit.Web.Services;

/// <summary>Выбранные предмет и семестр — общие для страниц ДЗ, студентов и таблицы.</summary>
public class Prefs
{
    public int? SubjectId { get; set; }
    public SemesterDto? Semester { get; set; }

    /// <summary>Строки сводной таблицы в текущем фильтре — для «Следующий студент» на экране проверки.</summary>
    public GradebookDto? LastGradebook { get; set; }
}
