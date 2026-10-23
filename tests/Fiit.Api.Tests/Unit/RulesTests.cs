using Fiit.Api.Domain;
using Fiit.Contracts;

namespace Fiit.Api.Tests.Unit;

public class GroupNameTests
{
    [Theory]
    [InlineData("фт 201", null, "ФТ-201", null)]
    [InlineData("ФТ-201-1", null, "ФТ-201", 1)]
    [InlineData("  фиит–301 ", 2, "ФИИТ-301", 2)]
    [InlineData("ФТ-201", 1, "ФТ-201", 1)]
    [InlineData("фт201-3", null, "ФТ-201", 3)]
    public void Parses_and_normalizes(string input, int? subgroup, string group, int? expectedSubgroup)
    {
        Assert.Equal((group, expectedSubgroup), GroupName.Parse(input, subgroup));
    }

    [Theory]
    [InlineData("201", null)]
    [InlineData("ФТ", null)]
    [InlineData("ФТ-201-1", 2)]
    [InlineData("ФТ-201", 0)]
    public void Rejects_invalid(string input, int? subgroup) => Assert.Null(GroupName.Parse(input, subgroup));
}

public class SemestersTests
{
    [Theory]
    [InlineData("2026-09-01T00:00:00+05:00", 2026, SemesterTerm.Autumn)]
    [InlineData("2026-12-31T23:00:00+05:00", 2026, SemesterTerm.Autumn)]
    [InlineData("2027-01-20T10:00:00+05:00", 2026, SemesterTerm.Autumn)]
    [InlineData("2027-02-01T00:00:00+05:00", 2027, SemesterTerm.Spring)]
    [InlineData("2027-08-31T23:59:00+05:00", 2027, SemesterTerm.Spring)]
    // 31 августа 20:00 UTC — уже 1 сентября в Екатеринбурге
    [InlineData("2026-08-31T20:00:00+00:00", 2026, SemesterTerm.Autumn)]
    public void Current_semester_by_ekaterinburg_date(string now, int year, SemesterTerm term) =>
        Assert.Equal(new SemesterDto(year, term), Semesters.At(DateTimeOffset.Parse(now)));
}

public class PersonNameTests
{
    [Theory]
    [InlineData("  Иванов ", "Иванов")]
    [InlineData("Римский-Корсаков", "Римский-Корсаков")]
    [InlineData("Анна  Мария", "Анна Мария")]
    public void Accepts(string input, string expected) => Assert.Equal(expected, PersonName.Normalize(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Иванов1")]
    [InlineData("-Иванов")]
    [InlineData("Ив@нов")]
    public void Rejects(string input) => Assert.Null(PersonName.Normalize(input));

    [Fact]
    public void Rejects_too_long() => Assert.Null(PersonName.Normalize(new string('а', 101)));
}

public class GradingTests
{
    private static readonly DateTimeOffset Deadline = new(2026, 10, 10, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Total_is_override_or_sum()
    {
        Assert.Equal(7.5m, Grading.Total(null, [5m, 2.5m]));
        Assert.Equal(3m, Grading.Total(3m, [5m, 2.5m]));
        Assert.Null(Grading.Total(null, []));
    }

    [Fact]
    public void Cell_statuses()
    {
        var before = Deadline.AddHours(-1);
        var after = Deadline.AddHours(1);
        Assert.Equal(CellStatus.NotAssigned, Grading.Cell(false, null, Deadline, before));
        Assert.Equal(CellStatus.NotSubmitted, Grading.Cell(true, null, Deadline, before));
        Assert.Equal(CellStatus.Overdue, Grading.Cell(true, null, Deadline, after));

        var submission = new Submission { FileKey = "k", FileName = "f.pdf", SubmittedAt = before };
        Assert.Equal(CellStatus.OnReview, Grading.Cell(true, submission, Deadline, after));

        submission.Review = new Review { DraftHash = "a" };
        Assert.Equal(CellStatus.OnReview, Grading.Cell(true, submission, Deadline, after));

        submission.Review.SentAt = after;
        submission.Review.SentHash = "a";
        Assert.Equal(CellStatus.Reviewed, Grading.Cell(true, submission, Deadline, after));

        submission.Review.DraftHash = "b";
        Assert.Equal(CellStatus.ReviewedWithUnsentChanges, Grading.Cell(true, submission, Deadline, after));
    }

    [Fact]
    public void Lateness_is_relative_to_current_deadline()
    {
        Assert.True(Grading.IsLate(Deadline.AddSeconds(1), Deadline));
        Assert.False(Grading.IsLate(Deadline, Deadline));
    }

    [Fact]
    public void Average_ignores_missing_scores() => Assert.Equal(7.5m, Grading.Average([5m, null, 10m]));
}

public class RecipientsTests
{
    private static StudentFacts Student(int id, int[] courses, int[]? tags = null) => new(id, courses.ToHashSet(), (tags ?? []).ToHashSet());

    [Fact]
    public void Without_targets_all_students_of_homework_courses()
    {
        Assert.True(Recipients.Matches([1, 2], [], Student(10, [2])));
        Assert.False(Recipients.Matches([1, 2], [], Student(10, [3])));
    }

    [Fact]
    public void Targets_narrow_within_homework_courses()
    {
        HomeworkTarget[] targets =
        [
            new() { Kind = HomeworkTargetKind.Student, RefId = 10 },
            new() { Kind = HomeworkTargetKind.Tag, RefId = 7 },
            new() { Kind = HomeworkTargetKind.Course, RefId = 2 },
        ];
        Assert.True(Recipients.Matches([1, 2], targets, Student(10, [1])));
        Assert.True(Recipients.Matches([1, 2], targets, Student(11, [1], [7])));
        Assert.True(Recipients.Matches([1, 2], targets, Student(12, [2])));
        Assert.False(Recipients.Matches([1, 2], targets, Student(13, [1])));
        // Метка не выводит за пределы курсов ДЗ
        Assert.False(Recipients.Matches([1, 2], targets, Student(14, [3], [7])));
    }
}

public class DisplayTests
{
    [Fact]
    public void Formats_in_ekaterinburg_time() =>
        Assert.Equal("05.10 14:30", Display.Short(new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.Zero)));

    [Fact]
    public void Short_teacher_name() => Assert.Equal("Иванов И. И.", Fiit.Api.Services.Notifier.ShortName("Иванов Иван Иванович"));
}
