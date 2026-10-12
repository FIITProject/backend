namespace Fiit.Api.Domain;

public class Teacher
{
    public int Id { get; set; }
    public required string Login { get; set; }
    public required string PasswordHash { get; set; }
    public required string FullName { get; set; }
}
