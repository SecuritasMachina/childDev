using LevelUp.Data;
using LevelUp.Models;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

/// <summary>
/// Final coverage push: GoalEntry remaining commands, JournalList refresh/filter, GoalList pin, TaskExtensions.
/// </summary>
public class GoalEntryRemainingTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task ReopenGoal_SetsIsCompletedFalse()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Run 5k", EnteredDate = ts, CompletionDate = ts
        };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.True(vm.IsCompleted);
        await vm.ReopenCommand.ExecuteAsync(null);
        Assert.False(vm.IsCompleted);
    }

    [Fact]
    public async Task ReopenGoal_NoGuid_DoesNothing()
    {
        var vm = BuildVm();
        await vm.ReopenCommand.ExecuteAsync(null); // no exception
    }

    [Fact]
    public async Task CompleteLinkedTodo_RemovesFromList()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Learn guitar", EnteredDate = ts
        };
        await GoalRepo.SaveAsync(goal);

        var todo = new Todo
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Title = "Practice chords", Notes = "Goal: Learn guitar", UpdatedOn = ts
        };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.Single(vm.LinkedTodos);
        await vm.CompleteLinkedTodoCommand.ExecuteAsync(vm.LinkedTodos[0]);
        Assert.Empty(vm.LinkedTodos);
        Assert.False(vm.HasLinkedTodos);
    }

    [Fact]
    public async Task ShareProgress_NoGuid_DoesNotThrow()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.ShareProgressCommand.ExecuteAsync(null); // Guid empty — returns early
    }

    [Fact]
    public async Task ShareProgress_WithGuid_ExecutesInNoMauiMode()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Read 12 books", EnteredDate = ts
        };
        await GoalRepo.SaveAsync(goal);
        await GoalProgressRepo.SaveAsync(new GoalProgress
        {
            Guid = Guid.NewGuid().ToString(), GoalFk = goal.Guid, AccountFk = account.Guid,
            NextStepItems = "Finish chapter 3", UpdatedOn = ts
        });

        var vm = BuildVm();
        vm.GoalText = "Read 12 books";
        vm.Guid = goal.Guid;
        vm.MeasurableOutcome = "12 books in 12 months";
        await Task.Delay(200);

        await vm.ShareProgressCommand.ExecuteAsync(null); // should not throw in NO_MAUI
    }

    [Fact]
    public void SetNoteTemplate_SetsPrefix()
    {
        var vm = BuildVm();
        vm.SetNoteTemplateCommand.Execute("A win today: ");
        Assert.Equal("A win today: ", vm.NextStepItems);
    }

    [Fact]
    public void SetNoteTemplate_AlreadyHasPrefix_DoesNotDuplicate()
    {
        var vm = BuildVm();
        vm.NextStepItems = "A win today: Did something great";
        vm.SetNoteTemplateCommand.Execute("A win today: ");
        Assert.Equal("A win today: Did something great", vm.NextStepItems);
    }

    [Fact]
    public void ProgressBarValue_ReflectsPercent()
    {
        var vm = BuildVm();
        vm.ProgressPercent = 75;
        Assert.Equal(0.75, vm.ProgressBarValue, 2);
    }

    [Fact]
    public async Task Load_WithLinkedTodosAndProgressHistory_PopulatesCollections()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Exercise daily", EnteredDate = ts
        };
        await GoalRepo.SaveAsync(goal);

        for (int i = 0; i < 3; i++)
        {
            await GoalProgressRepo.SaveAsync(new GoalProgress
            {
                Guid = Guid.NewGuid().ToString(), GoalFk = goal.Guid, AccountFk = account.Guid,
                NextStepItems = $"Note {i}", UpdatedOn = ts + i
            });
        }

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.Equal(3, vm.ProgressNotesCount);
        Assert.True(vm.HasProgressHistory);
    }
}

public class GoalListRemainingTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task TogglePin_PinsGoal()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Pin me", EnteredDate = ts, IsPinned = false
        };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Goals);
        Assert.False(vm.Goals[0].IsPinned);

        await vm.TogglePinCommand.ExecuteAsync(vm.Goals[0]);

        var updated = await GoalRepo.GetAsync(goal.Guid);
        Assert.True(updated!.IsPinned);
    }

    [Fact]
    public async Task TogglePin_UnpinsGoal()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Already pinned", EnteredDate = ts, IsPinned = true
        };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.TogglePinCommand.ExecuteAsync(vm.Goals[0]);

        var updated = await GoalRepo.GetAsync(goal.Guid);
        Assert.False(updated!.IsPinned);
    }
}

public class JournalListRemainingTests : ViewModelTestBase
{
    private JournalListViewModel BuildVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task FilterText_NarrowsJournals()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Piano practice", EnteredDate = ts });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Went for a run", EnteredDate = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Journals.Count);

        vm.FilterText = "piano";
        Assert.Single(vm.Journals);
    }

    [Fact]
    public async Task DateFilter_Week_FiltersOldEntries()
    {
        var account = await CreateTestAccountAsync();
        var recentMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var oldMs = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Recent", EnteredDate = recentMs });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Old", EnteredDate = oldMs });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Journals.Count);

        vm.SetDateFilterCommand.Execute("Week");
        // Old entry should be filtered out
        Assert.Single(vm.Journals);
    }

    [Fact]
    public async Task DateFilter_Month_FiltersVeryOldEntries()
    {
        var account = await CreateTestAccountAsync();
        var recentMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var veryOldMs = DateTimeOffset.UtcNow.AddDays(-45).ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Recent", EnteredDate = recentMs });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Very old", EnteredDate = veryOldMs });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Journals.Count);

        vm.SetDateFilterCommand.Execute("Month");
        Assert.Single(vm.Journals);
    }

    [Fact]
    public async Task Refresh_NoAccount_SetsIsRefreshingFalse()
    {
        // No account created
        var vm = BuildVm();
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.False(vm.IsRefreshing);
    }

    [Fact]
    public async Task Refresh_WithAccount_LoadsJournals()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Entry", EnteredDate = ts });

        var vm = BuildVm();
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Single(vm.Journals);
        Assert.False(vm.IsRefreshing);
    }
}

public class TaskExtensionsTests
{
    [Fact]
    public async Task FireAndForget_SuccessfulTask_Completes()
    {
        var completed = false;
        async Task DoWork() { await Task.Delay(10); completed = true; }
        DoWork().FireAndForget();
        await Task.Delay(100);
        Assert.True(completed);
    }

    [Fact]
    public async Task FireAndForget_ThrowingTask_DoesNotPropagateException()
    {
        async Task ThrowWork() { await Task.Delay(10); throw new InvalidOperationException("test"); }
        ThrowWork().FireAndForget(); // should not throw
        await Task.Delay(100);
        // reaching here = exception was swallowed
    }
}
