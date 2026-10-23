using System.Text.RegularExpressions;

namespace Fiit.Api.Domain;

/// <summary>
/// Нормализация группы (П-2.2): верхний регистр, без лишних пробелов, единый дефис.
/// <c>фт 201</c> → <c>ФТ-201</c>; <c>ФТ-201-1</c> раскладывается на группу <c>ФТ-201</c> и подгруппу <c>1</c>.
/// </summary>
public static partial class GroupName
{
    [GeneratedRegex(@"^([A-ZА-ЯЁ]+)[\s\-]*(\d+)(?:[\s\-]+(\d+))?$")]
    private static partial Regex Pattern();

    public static (string Group, int? Subgroup)? Parse(string input, int? subgroup)
    {
        var s = input.Trim().ToUpperInvariant().Replace('–', '-').Replace('—', '-').Replace('‐', '-');
        var m = Pattern().Match(s);
        if (!m.Success)
            return null;

        var group = $"{m.Groups[1].Value}-{m.Groups[2].Value}";
        int? fromInput = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : null;
        if (fromInput is not null && subgroup is not null && fromInput != subgroup)
            return null;
        var number = fromInput ?? subgroup;
        if (number is <= 0)
            return null;
        return (group, number);
    }
}
