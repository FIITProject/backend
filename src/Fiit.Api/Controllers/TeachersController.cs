using Fiit.Api.Data;
using Fiit.Api.Domain;
using Fiit.Api.Services;
using Fiit.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Controllers;

/// <summary>П-10: преподаватели. Добавлять и сбрасывать пароль может любой, отключать — только тот, кто добавил.</summary>
[ApiController]
[Route("api/teachers")]
public class TeachersController(AppDbContext db, Access access, TimeProvider time) : ControllerBase
{
    [HttpGet]
    public async Task<TeacherDto[]> List(CancellationToken ct)
    {
        var me = access.Me;
        return await db.Teachers
            .OrderBy(t => t.FullName)
            .Select(t => new TeacherDto(t.Id, t.FullName, t.Login, t.AddedById, t.AddedBy!.FullName, t.CreatedAt, t.DisabledAt != null, t.AddedById == me))
            .ToArrayAsync(ct);
    }

    [HttpPost]
    public async Task<TempPasswordResponse> Create(CreateTeacherRequest request, CancellationToken ct)
    {
        var login = request.Login.Trim();
        var fullName = string.Join(' ', request.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (login.Length is 0 or > 100 || fullName.Length is 0 or > 200)
            throw ApiException.BadRequest("Укажите ФИО и логин");
        if (await db.Teachers.AnyAsync(t => t.Login == login, ct))
            throw ApiException.Conflict($"Логин «{login}» уже занят");

        var password = Secrets.GenerateTemporary();
        var now = time.GetUtcNow();
        var teacher = new Teacher
        {
            Login = login,
            FullName = fullName,
            PasswordHash = Secrets.Hash(password),
            MustChangePassword = true,
            AddedById = access.Me,
            CreatedAt = now,
            SessionsValidFrom = now,
        };
        db.Teachers.Add(teacher);
        await db.SaveChangesAsync(ct);
        return new TempPasswordResponse(teacher.Id, login, password);
    }

    /// <summary>П-10.3: новый временный пароль коллеге; его текущие сессии завершаются.</summary>
    [HttpPost("{id:int}/reset-password")]
    public async Task<TempPasswordResponse> ResetPassword(int id, CancellationToken ct)
    {
        if (id == access.Me)
            throw ApiException.BadRequest("Свой пароль меняется в профиле");
        var teacher = await db.Teachers.FindAsync([id], ct) ?? throw ApiException.NotFound("Преподаватель не найден");

        var password = Secrets.GenerateTemporary();
        teacher.PasswordHash = Secrets.Hash(password);
        teacher.MustChangePassword = true;
        teacher.SessionsValidFrom = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return new TempPasswordResponse(teacher.Id, teacher.Login, password);
    }

    [HttpPost("{id:int}/disable")]
    public Task Disable(int id, CancellationToken ct) => SetDisabled(id, true, ct);

    [HttpPost("{id:int}/enable")]
    public Task Enable(int id, CancellationToken ct) => SetDisabled(id, false, ct);

    /// <summary>П-10.4: только тот, кто добавил. Отключённый не может войти, его курсы, ДЗ и проверки сохраняются.</summary>
    private async Task SetDisabled(int id, bool disabled, CancellationToken ct)
    {
        var teacher = await db.Teachers.FindAsync([id], ct) ?? throw ApiException.NotFound("Преподаватель не найден");
        if (teacher.AddedById != access.Me)
            throw ApiException.Forbidden("Отключить преподавателя может только тот, кто его добавил");

        var now = time.GetUtcNow();
        teacher.DisabledAt = disabled ? teacher.DisabledAt ?? now : null;
        if (disabled)
            teacher.SessionsValidFrom = now;
        await db.SaveChangesAsync(ct);
    }
}
