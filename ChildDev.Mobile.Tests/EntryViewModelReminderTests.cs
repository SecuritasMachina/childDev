using LevelUp.Data;
using LevelUp.Models;
using LevelUp.Services;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

/// <summary>
/// Tests for SetReminder, FormatDuration, and miscellaneous entry VM paths not covered elsewhere.
/// </summary>
public class EntryViewModelReminderTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildGoalVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    private TodoEntryViewModel BuildTodoVm() =>
        new(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);

    private JournalEntryViewModel BuildJournalVm() =>
        new(JournalRepo, AccountService, Analytics, Nav, ReminderSvc);

    // --- GoalEntryViewModel ---

    [Fact]
    public async Task GoalEntry_SetReminder_NoGuid_DoesNotSchedule()
    {
        await CreateTestAccountAsync();
        var vm = BuildGoalVm(); // Guid empty
        Nav.ActionSheetResult = "1 hour";
        await vm.SetReminderCommand.ExecuteAsync(null);

        Assert.Empty(NotificationService.Scheduled);
    }

    [Fact]
    public async Task GoalEntry_SetReminder_WithGuid_SchedulesReminder()
    {
        var account = await CreateTestAccountAsync();
        var goal = new Goal
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            GoalText = "Learn piano",
            EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await GoalRepo.SaveAsync(goal);
        await Task.Delay(100); // let Guid setter trigger Load

        var vm = BuildGoalVm();
        vm.GoalText = "Learn piano";
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Nav.ActionSheetResult = "1 hour";
        await vm.SetReminderCommand.ExecuteAsync(null);

        Assert.Single(NotificationService.Scheduled);
        Assert.Contains("Goal:", NotificationService.Scheduled[0].Title);
    }

    [Fact]
    public async Task GoalEntry_SetReminder_UserCancels_DoesNotSchedule()
    {
        var account = await CreateTestAccountAsync();
        var goal = new Goal
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            GoalText = "Run 5k",
            EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildGoalVm();
        vm.GoalText = "Run 5k";
        vm.Guid = goal.Guid;
        Nav.ActionSheetResult = null; // user cancels snooze picker

        await vm.SetReminderCommand.ExecuteAsync(null);
        Assert.Empty(NotificationService.Scheduled);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(8.0)]
    [InlineData(24.0)]
    [InlineData(72.0)]
    public async Task GoalEntry_SetReminder_AlertShowsDuration(double hours)
    {
        var account = await CreateTestAccountAsync();
        var goal = new Goal
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            GoalText = "Goal",
            EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildGoalVm();
        vm.GoalText = "Goal";
        vm.Guid = goal.Guid;

        // Map hours to a snooze choice label
        var choice = hours switch
        {
            1 => "1 hour",
            8 => "8 hours",
            24 => "1 day",
            72 => "3 days",
            _ => "1 hour"
        };
        Nav.ActionSheetResult = choice;
        await vm.SetReminderCommand.ExecuteAsync(null);

        Assert.Contains("Reminder Set", Nav.AlertTitles);
    }

    // --- TodoEntryViewModel ---

    [Fact]
    public async Task TodoEntry_SetReminder_NoGuid_DoesNotSchedule()
    {
        await CreateTestAccountAsync();
        var vm = BuildTodoVm();
        vm.Title = "Test todo";
        Nav.ActionSheetResult = "1 hour";
        await vm.SetReminderCommand.ExecuteAsync(null);
        Assert.Empty(NotificationService.Scheduled);
    }

    [Fact]
    public async Task TodoEntry_SetReminder_WithGuid_SchedulesReminder()
    {
        var account = await CreateTestAccountAsync();
        var todo = new Todo
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            Title = "Buy milk",
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildTodoVm();
        vm.Title = "Buy milk";
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        Nav.ActionSheetResult = "8 hours";
        await vm.SetReminderCommand.ExecuteAsync(null);

        Assert.Single(NotificationService.Scheduled);
        Assert.Contains("Todo:", NotificationService.Scheduled[0].Title);
    }

    [Fact]
    public void TodoEntry_SetDueToday_SetsDateAndFlag()
    {
        var vm = BuildTodoVm();
        vm.SetDueTodayCommand.Execute(null);
        Assert.Equal(DateTime.Today, vm.DueDate);
        Assert.True(vm.HasDueDate);
    }

    [Fact]
    public void TodoEntry_SetDueTomorrow_SetsDateAndFlag()
    {
        var vm = BuildTodoVm();
        vm.SetDueTomorrowCommand.Execute(null);
        Assert.Equal(DateTime.Today.AddDays(1), vm.DueDate);
        Assert.True(vm.HasDueDate);
    }

    [Fact]
    public void TodoEntry_SetDueThisWeek_SetsFridayAndFlag()
    {
        var vm = BuildTodoVm();
        vm.SetDueThisWeekCommand.Execute(null);
        Assert.Equal(DayOfWeek.Friday, vm.DueDate.DayOfWeek);
        Assert.True(vm.HasDueDate);
    }

    [Fact]
    public async Task TodoEntry_MarkDone_NoGuid_DoesNotNavigate()
    {
        await CreateTestAccountAsync();
        var vm = BuildTodoVm();
        await vm.MarkDoneCommand.ExecuteAsync(null);
        Assert.Empty(Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task TodoEntry_MarkDone_WithGuid_CompletesAndNavigates()
    {
        var account = await CreateTestAccountAsync();
        var todo = new Todo
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            Title = "Buy milk",
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildTodoVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        await vm.MarkDoneCommand.ExecuteAsync(null);
        Assert.Contains("..", Nav.NavigatedRoutes);

        var saved = await TodoRepo.GetAsync(todo.Guid);
        Assert.NotNull(saved?.CompletedAt);
    }

    [Fact]
    public async Task TodoEntry_Restore_WithGuid_UncompletesAndNavigates()
    {
        var account = await CreateTestAccountAsync();
        var todo = new Todo
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            Title = "Buy milk",
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            CompletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildTodoVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        await vm.RestoreCommand.ExecuteAsync(null);
        Assert.Contains("..", Nav.NavigatedRoutes);

        var saved = await TodoRepo.GetAsync(todo.Guid);
        Assert.Null(saved?.CompletedAt);
    }

    [Fact]
    public async Task TodoEntry_Delete_Confirmed_DeletesAndNavigates()
    {
        var account = await CreateTestAccountAsync();
        var todo = new Todo
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            Title = "Buy milk",
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await TodoRepo.SaveAsync(todo);

        Nav.AlertConfirmResult = true;
        var vm = BuildTodoVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        await vm.DeleteCommand.ExecuteAsync(null);
        Assert.Contains("..", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task TodoEntry_Delete_Cancelled_DoesNotNavigate()
    {
        var account = await CreateTestAccountAsync();
        var todo = new Todo
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            Title = "Buy milk",
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await TodoRepo.SaveAsync(todo);

        Nav.AlertConfirmResult = false;
        var vm = BuildTodoVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        await vm.DeleteCommand.ExecuteAsync(null);
        Assert.DoesNotContain("..", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task TodoEntry_OnLinkedGoalChanged_PrependsPrefixToNotes()
    {
        var account = await CreateTestAccountAsync();
        var goal = new Goal
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            GoalText = "Learn guitar",
            EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildTodoVm();
        vm.LinkedGoal = goal;

        Assert.StartsWith("Goal: Learn guitar", vm.Notes);
    }

    // --- JournalEntryViewModel ---

    [Fact]
    public async Task JournalEntry_SetReminder_SchedulesReminder()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildJournalVm();
        vm.Notes = "Today I practiced scales";
        await vm.SaveCommand.ExecuteAsync(null);

        // Get the saved entry GUID
        var entries = await JournalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(entries);

        var vm2 = BuildJournalVm();
        vm2.Notes = "Today I practiced scales";
        vm2.Guid = entries[0].Guid;
        await Task.Delay(200);

        Nav.ActionSheetResult = "1 day";
        await vm2.SetReminderCommand.ExecuteAsync(null);

        Assert.Single(NotificationService.Scheduled);
        Assert.Contains("Journal:", NotificationService.Scheduled[0].Title);
    }

    [Fact]
    public async Task JournalEntry_SetReminder_NoAccount_DoesNotSchedule()
    {
        var vm = BuildJournalVm(); // no account
        vm.Notes = "Some notes";
        Nav.ActionSheetResult = "1 hour";
        await vm.SetReminderCommand.ExecuteAsync(null);
        Assert.Empty(NotificationService.Scheduled);
    }

    [Fact]
    public async Task JournalEntry_SetReminder_UserCancels_DoesNotSchedule()
    {
        await CreateTestAccountAsync();
        var vm = BuildJournalVm();
        vm.Notes = "Practice notes";
        Nav.ActionSheetResult = null;
        await vm.SetReminderCommand.ExecuteAsync(null);
        Assert.Empty(NotificationService.Scheduled);
    }

    [Fact]
    public void JournalEntry_SetMood_UpdatesMood()
    {
        var vm = BuildJournalVm();
        vm.SetMoodCommand.Execute("Happy");
        Assert.Equal("Happy", vm.Mood);
    }

    [Fact]
    public void JournalEntry_SetActivity_UpdatesActivity()
    {
        var vm = BuildJournalVm();
        vm.SetActivityCommand.Execute("Reading");
        Assert.Equal("Reading", vm.Activity);
    }

    [Fact]
    public void JournalEntry_ToggleTag_AddsTag()
    {
        var vm = BuildJournalVm();
        vm.ToggleTagCommand.Execute("Focus");
        Assert.Equal("Focus", vm.Tags);
    }

    [Fact]
    public void JournalEntry_ToggleTag_RemovesExistingTag()
    {
        var vm = BuildJournalVm();
        vm.ToggleTagCommand.Execute("Focus");
        vm.ToggleTagCommand.Execute("Focus");
        Assert.Empty(vm.Tags);
    }

    [Fact]
    public void JournalEntry_ToggleTag_MultipleTagsAddedAndRemoved()
    {
        var vm = BuildJournalVm();
        vm.ToggleTagCommand.Execute("Focus");
        vm.ToggleTagCommand.Execute("Exercise");
        Assert.Contains("Focus", vm.Tags);
        Assert.Contains("Exercise", vm.Tags);

        vm.ToggleTagCommand.Execute("Focus");
        Assert.DoesNotContain("Focus", vm.Tags);
        Assert.Contains("Exercise", vm.Tags);
    }

    [Fact]
    public async Task JournalEntry_Delete_NoGuid_DoesNotNavigate()
    {
        await CreateTestAccountAsync();
        var vm = BuildJournalVm();
        await vm.DeleteCommand.ExecuteAsync(null);
        Assert.Empty(Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task JournalEntry_Delete_Confirmed_DeletesAndNavigates()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildJournalVm();
        vm.Notes = "A journal entry";
        await vm.SaveCommand.ExecuteAsync(null);
        var entries = await JournalRepo.GetAllActiveAsync(account.Guid);

        Nav.AlertConfirmResult = true;
        var vm2 = BuildJournalVm();
        vm2.Guid = entries[0].Guid;
        await Task.Delay(200);

        await vm2.DeleteCommand.ExecuteAsync(null);
        Assert.Contains("..", Nav.NavigatedRoutes);
    }

    [Fact]
    public void JournalEntry_WordCount_UpdatesOnNotesChange()
    {
        var vm = BuildJournalVm();
        vm.Notes = "Hello world today";
        Assert.Equal(3, vm.NotesWordCount);
    }

    [Fact]
    public void JournalEntry_CanSave_NeitherNotesNorActivity_ReturnsFalse()
    {
        var vm = BuildJournalVm();
        vm.Notes = string.Empty;
        vm.Activity = string.Empty;
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void JournalEntry_CanSave_ActivityOnly_ReturnsTrue()
    {
        var vm = BuildJournalVm();
        vm.Notes = string.Empty;
        vm.Activity = "Reading";
        Assert.True(vm.SaveCommand.CanExecute(null));
    }
}
