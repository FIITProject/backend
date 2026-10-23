namespace Fiit.Api.Domain;

/// <summary>Предмет (дисциплина), например «Python». Общий для всех преподов; NormalizedName защищает от дублей «Python»/«python».</summary>
public class Subject
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }

    public static string Normalize(string name) => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
