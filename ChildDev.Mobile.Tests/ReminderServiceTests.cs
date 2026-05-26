using LevelUp.Models;
using LevelUp.Services;

namespace LevelUp.Tests;

public class ReminderServiceTests : ViewModelTestBase
{
    private Reminder BuildReminder(string accountFk, string title = "Test Reminder", string topic = "General") =>
        new()
        {
            AccountFk = accountFk,
            Title = title,
            Topic = topic,
            FireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
        };

    [Fact]
    public async Task Schedule_SavesReminderToDb()
    {
        var account = await CreateTestAccountAsync();
        var reminder = BuildReminder(account.Guid, "Check goals");

        await ReminderSvc.ScheduleAsync(reminder);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.Equal("Check goals", pending[0].Title);
    }

    [Fact]
    public async Task Schedule_SchedulesOsNotification()
    {
        var account = await CreateTestAccountAsync();
        var reminder = BuildReminder(account.Guid, "Journal reminder");

        await ReminderSvc.ScheduleAsync(reminder);

        Assert.Single(NotificationService.Scheduled);
        Assert.Equal("Journal reminder", NotificationService.Scheduled[0].Title);
    }

    [Fact]
    public async Task Schedule_AssignsNotificationId()
    {
        var account = await CreateTestAccountAsync();
        var reminder = BuildReminder(account.Guid);

        await ReminderSvc.ScheduleAsync(reminder);

        Assert.NotEqual(0, reminder.NotificationId);
        Assert.Equal(reminder.NotificationId, NotificationService.Scheduled[0].Id);
    }

    [Fact]
    public async Task Schedule_StoresReturningDataAsReminderGuid()
    {
        var account = await CreateTestAccountAsync();
        var reminder = BuildReminder(account.Guid);

        await ReminderSvc.ScheduleAsync(reminder);

        Assert.Equal(reminder.Guid, NotificationService.Scheduled[0].Data);
    }

    [Fact]
    public async Task Snooze_CancelsOldNotificationAndReschedulesNew()
    {
        var account = await CreateTestAccountAsync();
        var reminder = BuildReminder(account.Guid);
        await ReminderSvc.ScheduleAsync(reminder);
        var originalId = reminder.NotificationId;
        NotificationService.Scheduled.Clear();

        await ReminderSvc.SnoozeAsync(reminder, TimeSpan.FromHours(1));

        Assert.Contains(originalId, NotificationService.Cancelled);
        Assert.Single(NotificationService.Scheduled);
    }

    [Fact]
    public async Task Snooze_UpdatesFireAt()
    {
        var account = await CreateTestAccountAsync();
        var reminder = BuildReminder(account.Guid);
        var originalFireAt = reminder.FireAt;
        await ReminderSvc.ScheduleAsync(reminder);

        await ReminderSvc.SnoozeAsync(reminder, TimeSpan.FromHours(8));

        Assert.True(reminder.FireAt > originalFireAt);
        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.True(pending[0].FireAt > originalFireAt);
    }

    [Fact]
    public async Task Dismiss_CancelsNotificationAndMarksDismissed()
    {
        var account = await CreateTestAccountAsync();
        var reminder = BuildReminder(account.Guid);
        await ReminderSvc.ScheduleAsync(reminder);

        await ReminderSvc.DismissAsync(reminder);

        Assert.Contains(reminder.NotificationId, NotificationService.Cancelled);
        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task GetPending_ExcludesDismissed()
    {
        var account = await CreateTestAccountAsync();
        var r1 = BuildReminder(account.Guid, "Active");
        var r2 = BuildReminder(account.Guid, "Dismissed");
        await ReminderSvc.ScheduleAsync(r1);
        await ReminderSvc.ScheduleAsync(r2);
        await ReminderSvc.DismissAsync(r2);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.Equal("Active", pending[0].Title);
    }

    [Fact]
    public async Task GetForEntity_ReturnsOnlyMatchingEntityReminders()
    {
        var account = await CreateTestAccountAsync();
        var goalGuid = System.Guid.NewGuid().ToString();
        var goalReminder = new Reminder
        {
            AccountFk = account.Guid,
            Title = "Goal reminder",
            Topic = "Goal",
            EntityGuid = goalGuid,
            FireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
        };
        var generalReminder = BuildReminder(account.Guid, "General");
        await ReminderSvc.ScheduleAsync(goalReminder);
        await ReminderSvc.ScheduleAsync(generalReminder);

        var forEntity = await ReminderSvc.GetForEntityAsync(goalGuid);
        Assert.Single(forEntity);
        Assert.Equal("Goal reminder", forEntity[0].Title);
    }
}
