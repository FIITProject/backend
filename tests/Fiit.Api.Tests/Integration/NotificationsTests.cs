using System.Net;
using Fiit.Contracts;
using Fiit.Messaging;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace Fiit.Api.Tests.Integration;

/// <summary>Н-7: уведомление из outbox доходит до очереди бота в RabbitMQ, бот сообщает результат доставки.</summary>
[Collection(ApiCollection.Name)]
public class NotificationsTests(ApiFactory factory)
{
    [Fact]
    public async Task Outbox_is_published_to_bot_queue()
    {
        var (teacher, _, _) = await factory.TeacherAsync();
        var bot = factory.Bot();
        var subject = await Scenario.SubjectAsync(teacher);
        var course = await Scenario.CourseAsync(teacher, subject.Id);
        var invite = await Scenario.InviteAsync(teacher, subject.Id, course.Id);
        var (tgId, _) = await Scenario.ApprovedStudentAsync(teacher, bot, invite, course.Id);
        var approved = (await Scenario.DrainAsync(bot, tgId)).Single();

        var message = await ReceiveAsync(m => m.Id == approved.Id);

        Assert.Equal(tgId, message.TelegramUserId);
        Assert.Equal(NotificationKind.ApplicationApproved, message.Kind);
        Assert.Equal(approved.Text, message.Text);
        Assert.Equal(NotificationStatus.Published, (await bot.Get<BotNotificationStateDto>($"/api/bot/notifications/{approved.Id}")).Status);
    }

    [Fact]
    public async Task Bot_reports_delivery_and_blocking()
    {
        var (teacher, _, _) = await factory.TeacherAsync();
        var bot = factory.Bot();
        var subject = await Scenario.SubjectAsync(teacher);
        var course = await Scenario.CourseAsync(teacher, subject.Id);
        var invite = await Scenario.InviteAsync(teacher, subject.Id, course.Id);
        var (tgId, studentId) = await Scenario.ApprovedStudentAsync(teacher, bot, invite, course.Id);
        var approved = (await Scenario.DrainAsync(bot, tgId)).Single();

        await bot.PostAsync($"/api/bot/notifications/{approved.Id}/delivered", null).Ok();
        await bot.PostAsync($"/api/bot/notifications/{approved.Id}/delivered", null).Ok();
        Assert.Equal(NotificationStatus.Delivered, (await bot.Get<BotNotificationStateDto>($"/api/bot/notifications/{approved.Id}")).Status);

        await Scenario.PublishedHomeworkAsync(teacher, Scenario.Homework(subject.Id, [course.Id]));
        var hwNotice = (await Scenario.DrainAsync(bot, tgId)).Single();
        await bot.Post($"/api/bot/notifications/{hwNotice.Id}/failed", new BotDeliveryFailedRequest("Forbidden: bot was blocked by the user", true)).Ok();

        Assert.Equal(NotificationStatus.Failed, (await bot.Get<BotNotificationStateDto>($"/api/bot/notifications/{hwNotice.Id}")).Status);
        var enrollment = (await teacher.Get<EnrollmentDto[]>($"/api/enrollments?courseIds={course.Id}")).Single(e => e.StudentId == studentId);
        Assert.True(enrollment.BotBlocked);

        // Студент снова пишет боту — значок снимается
        await bot.GetAsync($"/api/bot/students/{tgId}/homeworks").Ok<BotHomeworksDto>();
        Assert.False((await teacher.Get<EnrollmentDto[]>($"/api/enrollments?courseIds={course.Id}")).Single(e => e.StudentId == studentId).BotBlocked);
    }

    [Fact]
    public async Task Publish_without_notification_keeps_homework_visible()
    {
        var (teacher, _, _) = await factory.TeacherAsync();
        var bot = factory.Bot();
        var subject = await Scenario.SubjectAsync(teacher);
        var course = await Scenario.CourseAsync(teacher, subject.Id);
        var invite = await Scenario.InviteAsync(teacher, subject.Id, course.Id);
        var (tgId, _) = await Scenario.ApprovedStudentAsync(teacher, bot, invite, course.Id);
        await Scenario.DrainAsync(bot, tgId);

        var hw = await teacher.Post("/api/homeworks", Scenario.Homework(subject.Id, [course.Id])).Ok<HomeworkDto>();
        var published = await teacher.PostAsync($"/api/homeworks/{hw.Id}/publish?notify=false", null).Ok<PublishResponse>();

        Assert.Equal(0, published.Notified);
        Assert.Empty(await Scenario.DrainAsync(bot, tgId));
        Assert.Single((await bot.Get<BotHomeworksDto>($"/api/bot/students/{tgId}/homeworks")).Active);
    }

    [Fact]
    public async Task Notification_endpoints_require_bot_key()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/bot/notifications/1")).StatusCode);
    }

    /// <summary>Читает очередь бота, пока не встретит нужное сообщение (остальные — от других тестов — подтверждаются и пропускаются).</summary>
    private async Task<BotNotificationDto> ReceiveAsync(Func<BotNotificationDto, bool> match)
    {
        var connectionFactory = new ConnectionFactory { Uri = new Uri(factory.RabbitUri) };
        await using var connection = await connectionFactory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var result = await channel.BasicGetAsync(NotificationQueues.Queue, autoAck: true);
            if (result is null)
            {
                await Task.Delay(200);
                continue;
            }
            var message = NotificationQueues.Deserialize(result.Body);
            Assert.Equal(message.Id.ToString(), result.BasicProperties.MessageId);
            if (match(message))
                return message;
        }
        throw new TimeoutException("Notification was not published to RabbitMQ");
    }
}
