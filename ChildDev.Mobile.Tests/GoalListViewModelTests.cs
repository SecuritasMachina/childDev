using LevelUp.Data;
using LevelUp.Models;
using LevelUp.Services;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

public class GoalListViewModelTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_WithNoAccount_DoesNotThrow()
    {
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Empty(vm.Goals);
    }

    [Fact]
    public async Task Load_PopulatesGoalsFromLocalDb()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn piano", EnteredDate = now });
        await GoalRepo.SaveAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run 5k", EnteredDate = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Goals.Count);
        Assert.True(vm.HasGoals);
    }

    [Fact]
    public async Task Load_DoesNotRequireNetwork()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Read books", EnteredDate = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Single(vm.Goals);
        Assert.Empty(vm.StatusMessage);
    }

    [Fact]
    public async Task FilterText_NarrowsGoalList()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn piano", EnteredDate = now });
        await GoalRepo.SaveAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run 5k", EnteredDate = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "piano";

        Assert.Single(vm.Goals);
        Assert.Equal("Learn piano", vm.Goals[0].GoalText);
    }

    [Fact]
    public async Task FilterText_CaseInsensitive()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn Piano", EnteredDate = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "PIANO";

        Assert.Single(vm.Goals);
    }

    [Fact]
    public async Task CategoryFilter_ShowsMatchingCategoryOnly()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Math homework", Category = "Academic", EnteredDate = now });
        await GoalRepo.SaveAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Daily walk", Category = "Health", EnteredDate = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.CategoryFilter = "Academic";

        Assert.Single(vm.Goals);
        Assert.Equal("Academic", vm.Goals[0].Category);
    }

    [Fact]
    public async Task CategoryFilter_All_ShowsAllGoals()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Math", Category = "Academic", EnteredDate = now });
        await GoalRepo.SaveAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", Category = "Health", EnteredDate = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.CategoryFilter = "Academic";
        vm.CategoryFilter = "All";

        Assert.Equal(2, vm.Goals.Count);
    }

    [Fact]
    public async Task Add_NavigatesToGoalEntry()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.AddCommand.ExecuteAsync(null);

        Assert.Contains("goals/entry", Nav.NavigatedRoutes);
        Assert.DoesNotContain(Nav.NavigatedRoutes, r => r.StartsWith("http"));
    }

    [Fact]
    public async Task Add_NoExceptionThrown_WhenAddCommandExecuted()
    {
        // Regression: crashing with "Relative routing to shell elements is currently not
        // supported" because Shell.GoToAsync was called with a plain route string instead
        // of the required "///goals/entry" absolute form. Fix is in MauiNavigationService.
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        var ex = await Record.ExceptionAsync(() => vm.AddCommand.ExecuteAsync(null));
        Assert.Null(ex);
        Assert.Single(Nav.NavigatedRoutes, r => r.Contains("goals/entry"));
    }

    [Fact]
    public async Task Open_NavigatesToGoalEntryWithGuid()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.OpenCommand.ExecuteAsync(vm.Goals[0]);

        Assert.Contains(Nav.NavigatedRoutes, r => r.Contains("goals/entry") && r.Contains(goal.Guid));
    }

    [Fact]
    public async Task Delete_Confirmed_RemovesGoalFromList()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now });

        Nav.AlertConfirmResult = true;
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(vm.Goals[0]);

        Assert.Empty(vm.Goals);
    }

    [Fact]
    public async Task Delete_Cancelled_KeepsGoalInList()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now });

        Nav.AlertConfirmResult = false;
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(vm.Goals[0]);

        Assert.Single(vm.Goals);
    }

    [Fact]
    public async Task QuickNote_Confirmed_SavesProgressNote()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = "Made 2 miles today";
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.QuickNoteCommand.ExecuteAsync(vm.Goals[0]);

        var notes = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Single(notes);
        Assert.Equal("Made 2 miles today", notes[0].NextStepItems);
    }

    [Fact]
    public async Task QuickNote_Cancelled_SavesNothing()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = null;
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.QuickNoteCommand.ExecuteAsync(vm.Goals[0]);

        var notes = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Empty(notes);
    }

    [Fact]
    public async Task Refresh_WithNoServer_UpdatesGoals()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now });

        var vm = BuildVm();
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Single(vm.Goals);
        Assert.False(vm.IsRefreshing);
    }
}
