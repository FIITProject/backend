using Fiit.Api.Data;
using Fiit.Api.Domain;
using Fiit.Api.Services;
using Fiit.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Controllers;

/// <summary>П-6: личные метки препода.</summary>
[ApiController]
[Route("api/tags")]
public class TagsController(AppDbContext db, Access access) : ControllerBase
{
    [HttpGet]
    public async Task<TagDto[]> List(CancellationToken ct) =>
        await db.Tags.Where(t => t.TeacherId == access.Me)
            .OrderBy(t => t.Name)
            .Select(t => new TagDto(t.Id, t.Name, db.StudentTags.Count(st => st.TagId == t.Id)))
            .ToArrayAsync(ct);

    [HttpPost]
    public async Task<TagDto> Create(TagNameRequest request, CancellationToken ct)
    {
        var name = await ValidName(request.Name, null, ct);
        var tag = db.Tags.Add(new Tag { TeacherId = access.Me, Name = name }).Entity;
        await db.SaveChangesAsync(ct);
        return new TagDto(tag.Id, tag.Name, 0);
    }

    [HttpPut("{id:int}")]
    public async Task Rename(int id, TagNameRequest request, CancellationToken ct)
    {
        var tag = await Mine(id, ct);
        tag.Name = await ValidName(request.Name, id, ct);
        await db.SaveChangesAsync(ct);
    }

    [HttpDelete("{id:int}")]
    public async Task Delete(int id, CancellationToken ct)
    {
        var tag = await Mine(id, ct);
        await db.StudentTags.Where(st => st.TagId == id).ExecuteDeleteAsync(ct);
        await db.HomeworkTargets.Where(t => t.Kind == HomeworkTargetKind.Tag && t.RefId == id).ExecuteDeleteAsync(ct);
        db.Tags.Remove(tag);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>П-6.3: повесить метку на выбранных студентов (только из своих курсов).</summary>
    [HttpPost("{id:int}/students")]
    public async Task Assign(int id, TagStudentsRequest request, CancellationToken ct)
    {
        await Mine(id, ct);
        var my = access.MyCourses().Select(c => c.Id);
        var allowed = await db.Enrollments
            .Where(e => request.StudentIds.Contains(e.StudentId) && my.Contains(e.CourseId))
            .Select(e => e.StudentId).Distinct().ToListAsync(ct);
        if (allowed.Count != request.StudentIds.Distinct().Count())
            throw ApiException.NotFound("Студент не найден");
        var existing = await db.StudentTags.Where(st => st.TagId == id && allowed.Contains(st.StudentId)).Select(st => st.StudentId).ToListAsync(ct);
        db.StudentTags.AddRange(allowed.Except(existing).Select(s => new StudentTag { TagId = id, StudentId = s }));
        await db.SaveChangesAsync(ct);
    }

    [HttpPost("{id:int}/students/remove")]
    public async Task Unassign(int id, TagStudentsRequest request, CancellationToken ct)
    {
        await Mine(id, ct);
        await db.StudentTags.Where(st => st.TagId == id && request.StudentIds.Contains(st.StudentId)).ExecuteDeleteAsync(ct);
    }

    private async Task<Tag> Mine(int id, CancellationToken ct) =>
        await db.Tags.FirstOrDefaultAsync(t => t.Id == id && t.TeacherId == access.Me, ct) ?? throw ApiException.NotFound("Метка не найдена");

    private async Task<string> ValidName(string raw, int? exceptId, CancellationToken ct)
    {
        var name = raw.Trim();
        if (name.Length is 0 or > 100)
            throw ApiException.BadRequest("Название метки — от 1 до 100 символов");
        if (await db.Tags.AnyAsync(t => t.TeacherId == access.Me && t.Name == name && t.Id != exceptId, ct))
            throw ApiException.Conflict($"Метка «{name}» уже есть");
        return name;
    }
}
