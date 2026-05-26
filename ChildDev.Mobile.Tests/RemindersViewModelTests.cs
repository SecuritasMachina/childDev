using LevelUp.Models;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

public class RemindersViewModelTests : ViewModelTestBase
{
    private RemindersViewModel BuildVm() =>
        new(ReminderSvc, AccountService, Nav);

    [Fact]
    public async Task Load_WithNoPendingReminders_HasRemindersIsFalse()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Empty(vm.Reminders);
        Assert.False(vm.HasReminders);
    }

    [Fact]
    public async Task Load_PopulatesPendingReminders()
    {
        var account = await CreateTestAccountAsync();
        var reminder = new Reminder
        {
            AccountFk = account.Guid,
            Title = "Check goals",
            Topic = "Goal",
            FireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
        };
        await ReminderSvc.ScheduleAsync(reminder);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Single(vm.Reminders);
        Assert.True(vm.HasReminders);
        Assert.Equal("Check goals", vm.Reminders[0].Title);
    }

    [Fact]
    public async Task Dismiss_RemovesReminderFromList()
    {
        var account = await CreateTestAccountAsync();
        var reminder = new Reminder
        {
            AccountFk = account.Guid,
            Title = "Test",
            Topic = "General",
            FireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
        };
        await ReminderSvc.ScheduleAsync(reminder);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.DismissCommand.ExecuteAsync(vm.Reminders[0]);

        Assert.Empty(vm.Reminders);
        Assert.False(vm.HasReminders);
    }

    [Fact]
    public async Task Snooze_PickerCancelled_DoesNotChangeReminder()
    {
        var account = await CreateTestAccountAsync();
        var reminder = new Reminder
        {
            AccountFk = account.Guid,
            Title = "Test",
            Topic = "General",
            FireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
        };
        await ReminderSvc.ScheduleAsync(reminder);
        var originalFireAt = reminder.FireAt;

        Nav.ActionSheetResult = null; // user cancels
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.SnoozeCommand.ExecuteAsync(vm.Reminders[0]);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.Equal(originalFireAt, pending[0].FireAt);
    }

    [Fact]
    public async Task Snooze_1Hour_UpdatesFireAt()
    {
        var account = await CreateTestAccountAsync();
        var reminder = new Reminder
        {
            AccountFk = account.Guid,
            Title = "Test",
            Topic = "General",
            FireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
        };
        await ReminderSvc.ScheduleAsync(reminder);
        var originalFireAt = reminder.FireAt;

        Nav.ActionSheetResult = "1 hour";
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.SnoozeCommand.ExecuteAsync(vm.Reminders[0]);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.True(pending[0].FireAt > originalFireAt);
    }

    [Fact]
    public void AddGeneral_CanExecute_FalseWhenTitleEmpty()
    {
        var vm = BuildVm();
        vm.NewReminderTitle = string.Empty;
        Assert.False(vm.AddGeneralCommand.CanExecute(null));
    }

    [Fact]
    public void AddGeneral_CanExecute_TrueWhenTitleSet()
    {
        var vm = BuildVm();
        vm.NewReminderTitle = "Remember to practice";
        Assert.True(vm.AddGeneralCommand.CanExecute(null));
    }

    [Fact]
    public async Task AddGeneral_Confirmed_CreatesReminderAndClearsTitle()
    {
        var account = await CreateTestAccountAsync();
        Nav.ActionSheetResult = "1 day";

        var vm = BuildVm();
        vm.NewReminderTitle = "Practice guitar";
        await vm.AddGeneralCommand.ExecuteAsync(null);

        Assert.Empty(vm.NewReminderTitle);
        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.Equal("Practice guitar", pending[0].Title);
        Assert.Equal("General", pending[0].Topic);
    }
}
