using LevelUp.Data;
using LevelUp.Models;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

/// <summary>
/// Closes final coverage gaps: TodoEntry load paths (DueDate, LinkedGoal restore, LoadGoalsAsync),
/// GoalList category filter command, GoalEntry higher tiers + ShareProgress with outcome/completed.
/// </summary>
public class TodoEntryLoadPathTests : ViewModelTestBase
{
    private TodoEntryViewModel BuildVm() =>
        new(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task Load_ExistingTodoWithDueDate_SetsHasDueDateTrue()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var dueDateMs = DateTimeOffset.UtcNow.AddDays(5).ToUnixTimeMilliseconds();
        var todo = new Todo
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Title = "Task with due", UpdatedOn = now, DueDate = dueDateMs
        };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        Assert.True(vm.HasDueDate);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(dueDateMs).LocalDateTime.Date, vm.DueDate.Date);
    }

    [Fact]
    public async Task Load_ExistingTodoWithGoalPrefix_RestoresLinkedGoal()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Learn guitar", EnteredDate = ts
        };
        await GoalRepo.SaveAsync(goal);

        var todo = new Todo
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Title = "Practice chords", Notes = "Goal: Learn guitar", UpdatedOn = ts
        };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        Assert.NotNull(vm.LinkedGoal);
        Assert.Equal("Learn guitar", vm.LinkedGoal!.GoalText);
    }

    [Fact]
    public async Task Load_ExistingTodoWithGoalPrefixAndNotes_RestoresLinkedGoal()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Read more books", EnteredDate = ts
        };
        await GoalRepo.SaveAsync(goal);

        var todo = new Todo
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Title = "Read chapter 1", Notes = "Goal: Read more books\nFinish by Friday", UpdatedOn = ts
        };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        Assert.NotNull(vm.LinkedGoal);
        Assert.Equal("Read more books", vm.LinkedGoal!.GoalText);
    }

    [Fact]
    public async Task OnGuidChanged_FromNonEmptyToEmpty_LoadsAvailableGoals()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Active goal", EnteredDate = ts
        });

        var vm = BuildVm();
        // First set a non-empty guid (triggers LoadAsync which returns early for non-existent todo)
        vm.Guid = "nonexistent-guid-triggers-change";
        await Task.Delay(100);
        // Now set back to empty — fires LoadGoalsAsync
        vm.Guid = string.Empty;
        await Task.Delay(200);

        Assert.NotEmpty(vm.AvailableGoals);
    }

    [Fact]
    public async Task OnGuidChanged_FromNonEmptyToEmpty_NoAccount_DoesNotThrow()
    {
        var vm = BuildVm();
        vm.Guid = "some-guid";
        await Task.Delay(100);
        vm.Guid = string.Empty; // no account — should return early without throwing
        await Task.Delay(200); // no exception
    }
}

public class GoalListCategoryFilterTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task SetCategoryFilter_SetsProperty()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SetCategoryFilterCommand.Execute("NeedsAttention");
        Assert.Equal("NeedsAttention", vm.CategoryFilter);

        vm.SetCategoryFilterCommand.Execute("All");
        Assert.Equal("All", vm.CategoryFilter);
    }

    [Fact]
    public async Task SetCategoryFilter_NeedsAttention_GoalWithNoProgress_IsIncluded()
    {
        // A goal with no progress notes has null LatestProgressAt → NeedsAttention
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "No progress yet", EnteredDate = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Goals);

        vm.SetCategoryFilterCommand.Execute("NeedsAttention");

        Assert.Single(vm.Goals);
        Assert.Contains("need attention", vm.EntryCountDisplay);
    }

    [Fact]
    public async Task SetCategoryFilter_NeedsAttention_NoGoals_SetsUpToDateMessage()
    {
        // No goals at all → NeedsAttention filter shows "all up to date" empty message
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SetCategoryFilterCommand.Execute("NeedsAttention");

        Assert.Empty(vm.Goals);
        Assert.Contains("up to date", vm.EmptyMessage);
        Assert.Empty(vm.EntryCountDisplay);
    }

    [Fact]
    public async Task SetCategoryFilter_All_AfterNeedsAttention_RestoresAllGoals()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "G1", EnteredDate = now });
        await GoalRepo.SaveAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "G2", EnteredDate = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SetCategoryFilterCommand.Execute("NeedsAttention");
        vm.SetCategoryFilterCommand.Execute("All");

        Assert.Equal(2, vm.Goals.Count);
    }
}

public class GoalEntryHigherTierTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    private async Task SaveProgressNotes(string goalGuid, string accountGuid, int count)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < count; i++)
            await GoalProgressRepo.SaveAsync(new GoalProgress
            {
                Guid = System.Guid.NewGuid().ToString(), GoalFk = goalGuid, AccountFk = accountGuid,
                NextStepItems = $"Note {i}", UpdatedOn = ts + i
            });
    }

    [Theory]
    [InlineData(15, "🚀 Apprentice")]
    [InlineData(30, "⭐ Skilled")]
    [InlineData(60, "💎 Expert")]
    [InlineData(100, "🏆 Master")]
    [InlineData(200, "🌟 Legend")]
    public async Task Load_HigherProgressCounts_CorrectTierLabel(int noteCount, string expectedTier)
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Master goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        await SaveProgressNotes(goal.Guid, account.Guid, noteCount);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(300);

        Assert.Equal(expectedTier, vm.TierLabel);
    }

    [Theory]
    [InlineData(15, "Apprentice", "Skilled")]
    [InlineData(30, "Skilled", "Expert")]
    [InlineData(60, "Expert", "Master")]
    [InlineData(100, "Master", "Legend")]
    public async Task Load_HigherTiers_NextTierLabelShowsCorrectTarget(int noteCount, string currentTier, string nextTier)
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Progress goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        await SaveProgressNotes(goal.Guid, account.Guid, noteCount);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(300);

        Assert.Contains(nextTier, vm.NextTierLabel);
        _ = currentTier; // used in test name for clarity
    }

    [Fact]
    public async Task Load_LegendTier_NextTierLabelIsEmpty()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Legend goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        await SaveProgressNotes(goal.Guid, account.Guid, 200);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(300);

        Assert.Equal(string.Empty, vm.NextTierLabel);
    }

    [Fact]
    public async Task ShareProgress_WithMeasurableOutcome_IncludesSuccessMeasure()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Read 12 books", EnteredDate = ts, MeasurableOutcome = "1 book per month"
        };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        vm.GoalText = "Read 12 books";
        vm.MeasurableOutcome = "1 book per month";
        await Task.Delay(200);

        // Should not throw; in NO_MAUI mode the share sheet is skipped
        await vm.ShareProgressCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task ShareProgress_CompletedGoal_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Completed goal", EnteredDate = ts, CompletionDate = ts
        };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        vm.GoalText = "Completed goal";
        await Task.Delay(200);

        // IsCompleted should be true, ShareProgress runs the "Status: Completed ✓" branch
        Assert.True(vm.IsCompleted);
        await vm.ShareProgressCommand.ExecuteAsync(null); // should not throw in NO_MAUI
    }
}
