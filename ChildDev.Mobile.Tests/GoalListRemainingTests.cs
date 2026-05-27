using LevelUp.Data;
using LevelUp.Models;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

/// <summary>
/// Covers GoalList Refresh, UpdateEntryCountDisplay (active+completed), QuickNote cancel path.
/// </summary>
public class GoalListAdditionalTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Refresh_NoAccount_SetsIsRefreshingFalse()
    {
        var vm = BuildVm();
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.False(vm.IsRefreshing);
    }

    [Fact]
    public async Task Refresh_WithAccount_LoadsGoals()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run 5k", EnteredDate = ts });

        var vm = BuildVm();
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Single(vm.Goals);
        Assert.False(vm.IsRefreshing);
    }

    [Fact]
    public async Task EntryCountDisplay_WithCompletedGoal_ShowsActiveAndCompleted()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Active goal", EnteredDate = ts });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Done goal", EnteredDate = ts, CompletionDate = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("1 active", vm.EntryCountDisplay);
        Assert.Contains("1 completed", vm.EntryCountDisplay);
    }

    [Fact]
    public async Task EntryCountDisplay_NoCompleted_ShowsGoalCount()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "One goal", EnteredDate = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.DoesNotContain("completed", vm.EntryCountDisplay);
        Assert.Contains("1 goal", vm.EntryCountDisplay);
    }

    [Fact]
    public async Task QuickNote_Cancelled_DoesNotSaveProgress()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run 5k", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = null; // user cancels
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.QuickNoteCommand.ExecuteAsync(vm.Goals[0]);

        var notes = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Empty(notes);
    }

    [Fact]
    public async Task QuickNote_EmptyString_DoesNotSaveProgress()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run 5k", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = "  "; // whitespace only
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.QuickNoteCommand.ExecuteAsync(vm.Goals[0]);

        var notes = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Empty(notes);
    }

    [Fact]
    public async Task EntryCountDisplay_PluralGoals_UsesGoals()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Goal A", EnteredDate = ts });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Goal B", EnteredDate = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Contains("2 goals", vm.EntryCountDisplay);
    }
}
