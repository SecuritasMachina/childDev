using LevelUp.Data;
using LevelUp.Models;
using LevelUp.Services;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

/// <summary>
/// Regression tests verifying that all core CRUD operations work with zero
/// network connectivity. These tests exist to prevent any future code change
/// from accidentally requiring the server for create/read/update/delete operations.
/// </summary>
public class OfflineCapabilityTests : ViewModelTestBase
{
    [Fact]
    public async Task FullOfflineCycle_CreateAndRetrieveGoal()
    {
        var account = await CreateTestAccountAsync();
        var vm = new GoalEntryViewModel(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav);
        vm.GoalText = "Learn piano";
        vm.Category = "Creative";
        vm.NextStepItems = "First lesson this week";

        await vm.SaveCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(goals);
        Assert.Equal("Learn piano", goals[0].GoalText);
        var notes = await GoalProgressRepo.GetForGoalAsync(goals[0].Guid);
        Assert.Single(notes);
        Assert.Equal("First lesson this week", notes[0].NextStepItems);
    }

    [Fact]
    public async Task FullOfflineCycle_CreateAndRetrieveJournal()
    {
        var account = await CreateTestAccountAsync();
        var vm = new JournalEntryViewModel(JournalRepo, AccountService, Analytics, Nav);
        vm.Notes = "Had a great practice session";
        vm.Mood = "Happy";
        vm.Activity = "Music";

        await vm.SaveCommand.ExecuteAsync(null);

        var journals = await JournalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(journals);
        Assert.Equal("Had a great practice session", journals[0].Notes);
        Assert.Equal("Happy", journals[0].Mood);
    }

    [Fact]
    public async Task FullOfflineCycle_CreateAndCompleteTodo()
    {
        var account = await CreateTestAccountAsync();
        var listVm = new TodoListViewModel(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await listVm.LoadCommand.ExecuteAsync(null);
        listVm.NewTodoTitle = "Practice scales";
        await listVm.AddCommand.ExecuteAsync(null);

        Assert.Single(listVm.Todos);
        await listVm.CompleteCommand.ExecuteAsync(listVm.Todos[0]);

        Assert.Empty(listVm.Todos);
        var completed = await TodoRepo.GetCompletedAsync(account.Guid);
        Assert.Single(completed);
        Assert.NotNull(completed[0].CompletedAt);
    }

    [Fact]
    public async Task FullOfflineCycle_GoalProgressNote()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run 5k", EnteredDate = now };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = "Did 2 miles";
        var listVm = new GoalListViewModel(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await listVm.LoadCommand.ExecuteAsync(null);
        await listVm.QuickNoteCommand.ExecuteAsync(listVm.Goals[0]);

        var notes = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Single(notes);
        Assert.Equal("Did 2 miles", notes[0].NextStepItems);
    }

    [Fact]
    public async Task SyncFailure_DoesNotAffectLocalData()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Day 1", EnteredDate = now, UpdatedOn = now });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Task", UpdatedOn = now });

        var offlineSync = BuildOfflineSyncService();
        var result = await offlineSync.RunAsync(account);

        Assert.Equal(SyncResult.NoServer, result);
        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        var journals = await JournalRepo.GetAllActiveAsync(account.Guid);
        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(goals);
        Assert.Single(journals);
        Assert.Single(todos);
    }

    [Fact]
    public async Task Navigation_NeverProducesExternalUrl()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Entry", EnteredDate = now, UpdatedOn = now });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Task", UpdatedOn = now });

        var goalListVm = new GoalListViewModel(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await goalListVm.LoadCommand.ExecuteAsync(null);
        await goalListVm.AddCommand.ExecuteAsync(null);
        await goalListVm.OpenCommand.ExecuteAsync(goalListVm.Goals[0]);

        var journalListVm = new JournalListViewModel(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await journalListVm.LoadCommand.ExecuteAsync(null);
        await journalListVm.AddCommand.ExecuteAsync(null);
        await journalListVm.OpenCommand.ExecuteAsync(journalListVm.Journals[0]);

        foreach (var route in Nav.NavigatedRoutes)
        {
            Assert.False(route.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
                $"Navigation to external URL detected: {route}");
            Assert.False(route.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
                $"Navigation to external URL detected: {route}");
        }
    }

    [Fact]
    public async Task MultipleEntities_AllOffline_AllPersist()
    {
        var account = await CreateTestAccountAsync();

        var goalVm = new GoalEntryViewModel(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav);
        goalVm.GoalText = "Play guitar";
        await goalVm.SaveCommand.ExecuteAsync(null);

        var journalVm = new JournalEntryViewModel(JournalRepo, AccountService, Analytics, Nav);
        journalVm.Notes = "Practiced for 30 minutes";
        await journalVm.SaveCommand.ExecuteAsync(null);

        var todoListVm = new TodoListViewModel(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await todoListVm.LoadCommand.ExecuteAsync(null);
        todoListVm.NewTodoTitle = "Learn chord G";
        await todoListVm.AddCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        var journals = await JournalRepo.GetAllActiveAsync(account.Guid);
        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(goals);
        Assert.Single(journals);
        Assert.Single(todos);
    }
}
