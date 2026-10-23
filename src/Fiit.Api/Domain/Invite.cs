using Fiit.Contracts;

namespace Fiit.Api.Domain;

/// <summary>Приглашение в бота <c>t.me/&lt;бот&gt;?start=&lt;код&gt;</c>: предмет, семестр и курсы (подгруппы), в которые можно записаться.</summary>
public class Invite
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public int AuthorId { get; set; }
    public Teacher Author { get; set; } = null!;
    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<Course> Courses { get; set; } = [];

    public InviteStatus StatusAt(DateTimeOffset now) =>
        ClosedAt is not null ? InviteStatus.Closed
        : ExpiresAt is { } e && e <= now ? InviteStatus.Expired
        : InviteStatus.Active;
}
