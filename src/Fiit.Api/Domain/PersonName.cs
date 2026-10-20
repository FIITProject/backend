using System.Text.RegularExpressions;

namespace Fiit.Api.Domain;

/// <summary>Валидация ФИО из анкеты (С-1.5): непустое, только буквы, пробел и дефис, до 100 символов.</summary>
public static partial class PersonName
{
    [GeneratedRegex(@"^[\p{L}]+(?:[ \-][\p{L}]+)*$")]
    private static partial Regex Pattern();

    public static string? Normalize(string? input)
    {
        if (input is null)
            return null;
        var s = string.Join(' ', input.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return s.Length is > 0 and <= 100 && Pattern().IsMatch(s) ? s : null;
    }
}
