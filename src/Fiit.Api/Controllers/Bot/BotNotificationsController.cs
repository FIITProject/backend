using Fiit.Api.Auth;
using Fiit.Api.Data;
using Fiit.Api.Services;
using Fiit.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Controllers.Bot;

/// <summary>
/// Уведомления приходят боту через RabbitMQ (Н-7). Здесь бот проверяет, не доставлено ли уже уведомление (повторная доставка из очереди),
/// сообщает результат и забирает PDF проверки.
/// </summary>
[ApiController]
[Route("api/bot/notifications")]
[Authorize(Policy = Policies.Bot)]
public class BotNotificationsController(AppDbContext db, IFileStorage storage, TimeProvider time) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<BotNotificationStateDto> State(int id, CancellationToken ct)
    {
        var n = await db.Notifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiException.NotFound();
        return new BotNotificationStateDto(n.Id, n.Status);
    }

    /// <summary>Отправлено в Telegram. Повторный вызов ничего не меняет.</summary>
    [HttpPost("{id:int}/delivered")]
    public async Task Delivered(int id, CancellationToken ct)
    {
        var n = await db.Notifications.FindAsync([id], ct) ?? throw ApiException.NotFound();
        if (n.Status == NotificationStatus.Delivered)
            return;
        n.Status = NotificationStatus.Delivered;
        n.DeliveredAt = time.GetUtcNow();
        n.LastError = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Доставить не удалось (попытки кончились или ошибка постоянная). Студент заблокировал бота — значок у преподавателя (С-3.2).</summary>
    [HttpPost("{id:int}/failed")]
    public async Task Failed(int id, BotDeliveryFailedRequest request, CancellationToken ct)
    {
        var n = await db.Notifications.FindAsync([id], ct) ?? throw ApiException.NotFound();
        if (n.Status == NotificationStatus.Delivered)
            return;
        n.Status = NotificationStatus.Failed;
        n.LastError = request.Error.Length > 1000 ? request.Error[..1000] : request.Error;
        if (request.BotBlocked)
            await db.Students.Where(s => s.Id == n.StudentId).ExecuteUpdateAsync(s => s.SetProperty(x => x.BotBlockedAt, time.GetUtcNow()), ct);
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

    /// <summary>Уведомления студента по порядку — для эмулятора бота на тестовом сайте, который не читает RabbitMQ.</summary>
    [HttpGet("~/api/bot/students/{telegramUserId:long}/notifications")]
    public async Task<BotNotificationDto[]> ForStudent(long telegramUserId, int afterId, CancellationToken ct)
    {
        var studentId = await db.Students.Where(s => s.TelegramUserId == telegramUserId).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
        if (studentId is null)
            return [];
        var list = await db.Notifications.AsNoTracking()
            .Where(n => n.StudentId == studentId && n.Id > afterId)
            .OrderBy(n => n.Id)
            .Take(100)
            .ToListAsync(ct);
        return list.Select(n => NotificationOutboxPublisher.ToDto(n, telegramUserId)).ToArray();
    }
}
