namespace Fiit.Api.Domain;

/// <summary>Учебная группа, например ФТ-201. Имя хранится нормализованным и уникально.</summary>
public class Group
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public List<Subgroup> Subgroups { get; set; } = [];
}

/// <summary>Подгруппа — часть группы, к предмету не привязана. Number = null — группа без деления.</summary>
public class Subgroup
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;
    public int? Number { get; set; }

    public static string FullName(string group, int? number) => number is null ? group : $"{group}-{number}";
}
