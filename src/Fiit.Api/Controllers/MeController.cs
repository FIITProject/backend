using Fiit.Api.Auth;
using Fiit.Api.Data;
using Fiit.Api.Services;
using Fiit.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Controllers;

[ApiController]
[Route("api/me")]
[Authorize(Policy = Policies.AnyTeacher)]
public class MeController(AppDbContext db, LiveEvents events) : ControllerBase
{
    [HttpGet]
    public async Task<MeResponse> Get(CancellationToken ct)
    {
        var t = await db.Teachers.SingleAsync(t => t.Id == User.TeacherId(), ct);
        return new MeResponse(t.Id, t.Login, t.FullName, t.MustChangePassword);
    }

    /// <summary>
    /// Живые события для колокольчика (Server-Sent Events): <c>event: changed</c>, в <c>data</c> — что изменилось
    /// (<see cref="LiveEventKinds"/>). Сразу после подключения приходит <c>event: ready</c>. Раз в 25 секунд — комментарий,
    /// чтобы прокси не закрывали соединение.
    /// </summary>
    [HttpGet("events")]
    public async Task Events(CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no"; // nginx: не буферизовать поток

        // Подписка и ожидание следующего события — до отключения вкладки (ct отменяется при закрытии соединения)
        var stream = events.SubscribeAsync(ct).GetAsyncEnumerator(ct);
        var next = stream.MoveNextAsync().AsTask();
        try
        {
            await Write("event: ready\ndata: \n\n", ct);
            while (true)
            {
                var heartbeat = Task.Delay(TimeSpan.FromSeconds(25), ct);
                if (await Task.WhenAny(next, heartbeat) == next)
                {
                    if (!await next) break;
                    await Write($"event: changed\ndata: {stream.Current}\n\n", ct);
                    next = stream.MoveNextAsync().AsTask();
                }
                else
                {
                    await heartbeat; // отмена — исключение, выходим
                    await Write(": ping\n\n", ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Вкладку закрыли — штатно
        }
        finally
        {
            // Итератор нельзя освободить, пока ждёт событие: сначала дожидаемся отменённого ожидания
            try { await next; } catch (OperationCanceledException) { }
            await stream.DisposeAsync();
        }
    }

    private async Task Write(string text, CancellationToken ct)
    {
        await Response.WriteAsync(text, ct);
        await Response.Body.FlushAsync(ct);
    }
}
