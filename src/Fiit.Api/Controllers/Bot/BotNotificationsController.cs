using Fiit.Api.Auth;
using Fiit.Api.Data;
using Fiit.Api.Domain;
using Fiit.Api.Services;
using Fiit.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Controllers.Bot;

/// <summary>
/// Outbox уведомлений (Н-7): бот забирает задачи с арендой, отправляет в Telegram и подтверждает.
/// Если бот упал, аренда истекает и задача возвращается в очередь.
/// </summary>
[ApiController]
[Route("api/bot/notifications")]
[Authorize(Policy = Policies.Bot)]
public class BotNotificationsController(AppDbContext db, IFileStorage storage, TimeProvider time) : ControllerBase
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);
    private const int MaxAttempts = 8;

    [HttpPost("claim")]
    public async Task<BotNotificationDto[]> Claim(ClaimRequest request, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var limit = Math.Clamp(request.Limit, 1, 100);
        var lockedUntil = now + Lease;
        var pending = nameof(NotificationStatus.Pending);
        var claimed = await db.Notifications.FromSql($"""
                UPDATE notifications SET locked_until = {lockedUntil}, attempts = attempts + 1
                WHERE id IN (
                    SELECT id FROM notifications
                    WHERE status = {pending} AND available_at <= {now} AND (locked_until IS NULL OR locked_until < {now})
                    ORDER BY id
                    LIMIT {limit}
                    FOR UPDATE SKIP LOCKED)
                RETURNING *
                """)
            .AsNoTracking()
            .ToListAsync(ct);

        var studentIds = claimed.Select(n => n.StudentId).Distinct().ToList();
        var telegram = await db.Students.Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.TelegramUserId, ct);
        return claimed.OrderBy(n => n.Id)
            .Select(n => new BotNotificationDto(n.Id, telegram[n.StudentId], n.Kind, n.Text, n.HomeworkId, n.FileKey is not null, n.FileName))
            .ToArray();
    }

    [HttpPost("{id:int}/ack")]
    public async Task Ack(int id, CancellationToken ct)
    {
        var n = await db.Notifications.FindAsync([id], ct) ?? throw ApiException.NotFound();
        n.Status = NotificationStatus.Sent;
        n.SentAt = time.GetUtcNow();
        n.LockedUntil = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Ошибка отправки: временная — повтор с экспоненциальной задержкой, постоянная (или исчерпаны попытки) — failed.</summary>
    [HttpPost("{id:int}/fail")]
    public async Task Fail(int id, FailRequest request, CancellationToken ct)
    {
        var n = await db.Notifications.FindAsync([id], ct) ?? throw ApiException.NotFound();
        var now = time.GetUtcNow();
        n.LastError = request.Error.Length > 1000 ? request.Error[..1000] : request.Error;
        n.LockedUntil = null;
        if (request.Permanent || request.BotBlocked || n.Attempts >= MaxAttempts)
            n.Status = NotificationStatus.Failed;
        else
            n.AvailableAt = now + TimeSpan.FromSeconds(10 * Math.Pow(2, n.Attempts - 1));
        if (request.BotBlocked)
            await db.Students.Where(s => s.Id == n.StudentId).ExecuteUpdateAsync(s => s.SetProperty(x => x.BotBlockedAt, now), ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>PDF с пометками для уведомления о проверке.</summary>
    [HttpGet("{id:int}/file")]
    public async Task<FileContentResult> File(int id, CancellationToken ct)
    {
        var n = await db.Notifications.FindAsync([id], ct) ?? throw ApiException.NotFound();
        if (n.FileKey is null)
            throw ApiException.NotFound("У уведомления нет файла");
        return File(await storage.GetAsync(n.FileKey, ct), "application/pdf", n.FileName ?? "проверка.pdf");
    }
}
