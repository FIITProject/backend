using System.Collections.Concurrent;
using Fiit.Contracts;

namespace Fiit.Bot.Core;

public enum Step
{
    Idle,
    ConfirmName,
    LastName,
    FirstName,
    Subgroup,
    Consent,
    Summary,
    WaitingPdf,
}

/// <summary>Где студент в диалоге. Живёт в памяти бота: после перезапуска незаконченная анкета начинается заново, всё остальное хранит API.</summary>
public class ChatState
{
    public Step Step { get; set; }
    public string? InviteCode { get; set; }
    public string SubjectName { get; set; } = "";
    public BotCourseOption[] Courses { get; set; } = [];
    public int? CourseId { get; set; }
    public string? LastName { get; set; }
    public string? FirstName { get; set; }
    public bool Known { get; set; }
    public int? AwaitingPdfFor { get; set; }
    public IncomingDocument? PendingPdf { get; set; }
    public string? LastMediaGroupId { get; set; }
    public bool WarnedAboutGroup { get; set; }
    public DateTimeOffset TouchedAt { get; set; }

    public void Reset()
    {
        Step = Step.Idle;
        CourseId = null;
        AwaitingPdfFor = null;
        PendingPdf = null;
    }
}

public interface IChatStateStore
{
    ChatState Get(long chatId);
}

/// <summary>Состояния в памяти; давно не тронутые (неделя) выкидываются, чтобы словарь не рос бесконечно.</summary>
public class InMemoryChatStateStore(TimeProvider time) : IChatStateStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);
    private readonly ConcurrentDictionary<long, ChatState> _states = new();
    private DateTimeOffset _lastSweep;

    public ChatState Get(long chatId)
    {
        var now = time.GetUtcNow();
        if (now - _lastSweep > TimeSpan.FromHours(1))
        {
            _lastSweep = now;
            foreach (var (id, s) in _states)
            {
                if (now - s.TouchedAt > Ttl)
                    _states.TryRemove(id, out _);
            }
        }
        var state = _states.GetOrAdd(chatId, _ => new ChatState());
        state.TouchedAt = now;
        return state;
    }
}
