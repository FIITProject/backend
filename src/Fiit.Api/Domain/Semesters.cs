using Fiit.Contracts;

namespace Fiit.Api.Domain;

public static class Semesters
{
    /// <summary>Текущий семестр по дате (по времени Екатеринбурга): сентябрь–январь — осень, февраль–август — весна. Январь относится к осени прошлого года.</summary>
    public static SemesterDto At(DateTimeOffset now)
    {
        var local = Display.Local(now);
        return local.Month switch
        {
            >= 9 => new SemesterDto(local.Year, SemesterTerm.Autumn),
            1 => new SemesterDto(local.Year - 1, SemesterTerm.Autumn),
            _ => new SemesterDto(local.Year, SemesterTerm.Spring),
        };
    }
}
