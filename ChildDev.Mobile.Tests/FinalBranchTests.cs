using LevelUp.Data;
using LevelUp.Models;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

/// <summary>
/// Covers remaining branch gaps: JournalList streak warning, TodoEntry DueDate save,
/// GoalEntry HasNextMeetingDate/HasExpirationDate load paths.
/// </summary>
public class JournalListStreakWarningTests : ViewModelTestBase
{
    private JournalListViewModel BuildVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    private async Task SaveEntriesOnDaysBack(string accountFk, int[] daysBack)
    {
        foreach (var d in daysBack)
        {
            var ts = DateTimeOffset.UtcNow.AddDays(-d).ToUnixTimeMilliseconds();
            await JournalRepo.SaveAsync(new Journal
            {
                Guid = Guid.NewGuid().ToString(),
                AccountFk = accountFk,
                Notes = $"Entry {d}",
                EnteredDate = ts
            });
        }
    }

    [Fact]
    public async Task Load_Streak3_NoTodayEntry_ShowsProtectWarning()
    {
        var account = await CreateTestAccountAsync();
        // 3-day streak ending yesterday (no entry today)
        await SaveEntriesOnDaysBack(account.Guid, [1, 2, 3]);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasStreakWarning);
        Assert.Contains("🛡️", vm.StreakWarning);
    }

    [Fact]
    public async Task Load_Streak7Plus_NoTodayEntry_ShowsDontBreakWarning()
    {
        var account = await CreateTestAccountAsync();
        // 7-day streak ending yesterday
        await SaveEntriesOnDaysBack(account.Guid, [1, 2, 3, 4, 5, 6, 7]);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasStreakWarning);
        Assert.Contains("⚠️", vm.StreakWarning);
        Assert.Contains("7-day", vm.StreakWarning);
    }

    [Fact]
    public async Task Load_HasTodayEntry_NoStreakWarning()
    {
        var account = await CreateTestAccountAsync();
        // Entry today — no warning even with a streak
        await SaveEntriesOnDaysBack(account.Guid, [0, 1, 2, 3]);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasStreakWarning);
        Assert.Empty(vm.StreakWarning);
    }

    [Fact]
    public async Task Load_ShortStreak_NoWarning()
    {
        var account = await CreateTestAccountAsync();
        // Only 2-day streak — below threshold of 3
        await SaveEntriesOnDaysBack(account.Guid, [1, 2]);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasStreakWarning);
    }
}

public class TodoEntrySaveBranchTests : ViewModelTestBase
{
    private TodoEntryViewModel BuildVm() =>
        new(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task Save_WithDueDate_PersistsDueDate()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.Title = "Task with due date";
        vm.HasDueDate = true;
        vm.DueDate = DateTime.Today.AddDays(3);

        await vm.SaveCommand.ExecuteAsync(null);

        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(todos);
        Assert.NotNull(todos[0].DueDate);
    }

    [Fact]
    public async Task Save_WithoutDueDate_DueDateIsNull()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.Title = "No due date";
        vm.HasDueDate = false;

        await vm.SaveCommand.ExecuteAsync(null);

        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(todos);
        Assert.Null(todos[0].DueDate);
    }

    [Fact]
    public async Task Save_ExistingTodo_UpdatesRecord()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Original", UpdatedOn = now };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        vm.Title = "Updated title";
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = await TodoRepo.GetAsync(todo.Guid);
        Assert.Equal("Updated title", saved!.Title);
    }

    [Fact]
    public async Task Save_WithNotes_PersistsNotes()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.Title = "Task";
        vm.Notes = "Some important notes";

        await vm.SaveCommand.ExecuteAsync(null);

        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Equal("Some important notes", todos[0].Notes);
    }

    [Fact]
    public async Task Save_BlankNotes_NullNotes()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.Title = "Task";
        vm.Notes = "   ";

        await vm.SaveCommand.ExecuteAsync(null);

        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Null(todos[0].Notes);
    }
}

public class GoalEntryLoadBranchTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task Load_GoalWithMeetingDate_SetsHasNextMeetingDateTrue()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var meetingTs = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds();
        var goal = new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Learn piano", EnteredDate = ts, NextMeetingDate = meetingTs
        };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.True(vm.HasNextMeetingDate);
    }

    [Fact]
    public async Task Load_GoalWithExpirationDate_SetsHasExpirationDateTrue()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expTs = DateTimeOffset.UtcNow.AddMonths(3).ToUnixTimeMilliseconds();
        var goal = new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Run 5k", EnteredDate = ts, ExpirationDate = expTs
        };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.True(vm.HasExpirationDate);
    }

    [Fact]
    public async Task Load_CompletedGoal_SetsIsCompletedTrue()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Done goal", EnteredDate = ts, CompletionDate = ts
        };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.True(vm.IsCompleted);
    }

    [Fact]
    public async Task Load_GoalWith5ProgressNotes_ShowsBeginnerTier()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        for (int i = 0; i < 5; i++)
            await GoalProgressRepo.SaveAsync(new GoalProgress
            {
                Guid = Guid.NewGuid().ToString(), GoalFk = goal.Guid, AccountFk = account.Guid,
                NextStepItems = $"Note {i}", UpdatedOn = ts + i
            });

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.Equal("🌱 Beginner", vm.TierLabel);
        Assert.Contains("more notes", vm.NextTierLabel);
    }

    [Fact]
    public async Task Save_WithMeasurableOutcome_PersistsOutcome()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.GoalText = "Read more";
        vm.MeasurableOutcome = "12 books per year";

        await vm.SaveCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(goals);
        Assert.Equal("12 books per year", goals[0].MeasurableOutcome);
    }
}
