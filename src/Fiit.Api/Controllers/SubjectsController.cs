using Fiit.Api.Data;
using Fiit.Api.Domain;
using Fiit.Api.Services;
using Fiit.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Controllers;

/// <summary>П-2.1: список предметов общий для всех; создание возвращает существующий, если имя совпадает без учёта регистра.</summary>
[ApiController]
[Route("api/subjects")]
public class SubjectsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<SubjectDto[]> List(string? q, CancellationToken ct)
    {
        var query = db.Subjects.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var n = Subject.Normalize(q);
            query = query.Where(s => s.NormalizedName.Contains(n));
        }
        return await query.OrderBy(s => s.Name).Select(s => new SubjectDto(s.Id, s.Name)).ToArrayAsync(ct);
    }

    [HttpPost]
    public async Task<SubjectDto> Create(CreateSubjectRequest request, CancellationToken ct)
    {
        var name = string.Join(' ', request.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (name.Length is 0 or > 200)
            throw ApiException.BadRequest("Укажите название предмета");
        var normalized = Subject.Normalize(name);
        var subject = await db.Subjects.FirstOrDefaultAsync(s => s.NormalizedName == normalized, ct);
        if (subject is null)
        {
            subject = new Subject { Name = name, NormalizedName = normalized };
            db.Subjects.Add(subject);
            await db.SaveChangesAsync(ct);
        }
        return new SubjectDto(subject.Id, subject.Name);
    }
}

[ApiController]
[Route("api/semesters")]
public class SemestersController(TimeProvider time) : ControllerBase
{
    [HttpGet("current")]
    public SemesterDto Current() => Semesters.At(time.GetUtcNow());
}
