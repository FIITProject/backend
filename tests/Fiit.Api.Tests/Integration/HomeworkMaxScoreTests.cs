using Fiit.Contracts;

namespace Fiit.Api.Tests.Integration;

/// <summary>П-7.2: макс. балл задаётся без подзадач; появились подзадачи с баллами — максимум пересчитывается по ним.</summary>
[Collection(ApiCollection.Name)]
public class HomeworkMaxScoreTests(ApiFactory factory)
{
    [Fact]
    public async Task Max_score_without_subtasks_then_recalculated_by_subtasks()
    {
        var (teacher, _, _) = await factory.TeacherAsync();
        var subject = await Scenario.SubjectAsync(teacher);
        var course = await Scenario.CourseAsync(teacher, subject.Id);
        var request = Scenario.Homework(subject.Id, [course.Id]) with { MaxScore = 10 };

        var hw = await teacher.Post("/api/homeworks", request).Ok<HomeworkDto>();
        Assert.Equal(10, hw.MaxScore);

        var withSubtasks = await teacher.Put($"/api/homeworks/{hw.Id}",
            request with { Subtasks = [new SubtaskDto(null, "Задача 1", 3), new SubtaskDto(null, "Задача 2", 4.5m)] }).Ok<HomeworkDto>();
        Assert.Equal(7.5m, withSubtasks.MaxScore);

        var withoutScores = await teacher.Put($"/api/homeworks/{hw.Id}",
            request with { MaxScore = 5, Subtasks = [new SubtaskDto(null, "Задача без балла", null)] }).Ok<HomeworkDto>();
        Assert.Equal(5, withoutScores.MaxScore);

        var invalid = await teacher.Put($"/api/homeworks/{hw.Id}", request with { MaxScore = 0 });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
