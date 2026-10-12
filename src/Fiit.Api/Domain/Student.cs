namespace Fiit.Api.Domain;

public class Student
{
    public int Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public long? TelegramUserId { get; set; }
    public string? TelegramUsername { get; set; }
}

public enum EnrollmentStatus
{
    Pending,
    Approved,
}

/// <summary>Студент в <see cref="Course"/>. Создаётся из заявки по форме, препод подтверждает или удаляет.</summary>
public class Enrollment
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public EnrollmentStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Tag
{
    public int Id { get; set; }
    public int TeacherId { get; set; }
    public required string Name { get; set; }
}

public class StudentTag
{
    public int TagId { get; set; }
    public int StudentId { get; set; }
}
