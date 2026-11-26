using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Fiit.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Fiit.Api.Services;

/// <summary>Что изменилось: сайт по событию перечитывает счётчики колокольчика.</summary>
public static class LiveEventKinds
{
    /// <summary>Заявки на вступление: новая, подтверждена, отклонена.</summary>
    public const string Applications = "applications";

    /// <summary>Сдачи: новая работа или новая версия.</summary>
    public const string Submissions = "submissions";

    /// <summary>Проверки: черновик сохранён, результат отправлен студенту.</summary>
    public const string Reviews = "reviews";
}

/// <summary>
/// Живые события для открытых вкладок сайта (SSE, <c>GET /api/me/events</c>). Рассылка в памяти процесса:
/// API пока один экземпляр. В событии нет данных, только вид: сайт перечитывает свои счётчики, а права проверяет API.
/// Пропущенное событие не страшно: сайт перечитывает счётчики и при переподключении.
/// </summary>
public sealed class LiveEvents
{
    private readonly Lock gate = new();
    private readonly HashSet<Channel<string>> subscribers = [];

    public void Publish(string kind)
    {
        lock (gate)
            foreach (var s in subscribers)
                s.Writer.TryWrite(kind);
    }

    public async IAsyncEnumerable<string> SubscribeAsync([EnumeratorCancellation] CancellationToken ct)
    {
        // Медленный клиент не держит память: копятся только последние события, остальные всё равно сводятся к «перечитать»
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.DropOldest });
        lock (gate) subscribers.Add(channel);
        try
        {
            await foreach (var kind in channel.Reader.ReadAllAsync(ct))
                yield return kind;
        }
        finally
        {
            lock (gate) subscribers.Remove(channel);
        }
    }

    public int SubscriberCount { get { lock (gate) return subscribers.Count; } }
}

/// <summary>
/// После успешного SaveChanges публикует, что изменились заявки, сдачи или проверки. Один перехватчик на все
/// места изменения: новый сценарий не нужно помнить «подключить к колокольчику».
/// </summary>
public sealed class LiveEventsInterceptor(LiveEvents events) : SaveChangesInterceptor
{
    private readonly ConditionalWeakTable<DbContext, HashSet<string>> pending = [];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Flush(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Flush(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Discard(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Discard(eventData.Context);
        return Task.CompletedTask;
    }

    private void Collect(DbContext? context)
    {
        if (context is null) return;
        var kinds = new HashSet<string>();
        foreach (var e in context.ChangeTracker.Entries())
        {
            if (e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            var kind = e.Entity switch
            {
                Enrollment => LiveEventKinds.Applications,
                Submission => LiveEventKinds.Submissions,
                Review => LiveEventKinds.Reviews,
                _ => null,
            };
            if (kind is not null) kinds.Add(kind);
        }
        if (kinds.Count > 0) pending.AddOrUpdate(context, kinds);
        else pending.Remove(context);
    }

    private void Flush(DbContext? context)
    {
        if (context is null || !pending.TryGetValue(context, out var kinds)) return;
        pending.Remove(context);
        foreach (var kind in kinds) events.Publish(kind);
    }

    private void Discard(DbContext? context)
    {
        if (context is not null) pending.Remove(context);
    }
}
