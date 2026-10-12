namespace Fiit.Api.Domain;

/// <summary>Предмет (дисциплина), например «Python».</summary>
public class Subject
{
    public int Id { get; set; }
    public required string Name { get; set; }
}
