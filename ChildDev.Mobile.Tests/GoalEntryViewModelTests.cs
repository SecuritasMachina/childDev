using LevelUp.Data;
using LevelUp.Models;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

public class GoalEntryViewModelTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav);

    [Fact]
    public void CanSave_EmptyGoalText_ReturnsFalse()
    {
        var vm = BuildVm();
        vm.GoalText = string.Empty;
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void CanSave_WithGoalText_ReturnsTrue()
    {
        var vm = BuildVm();
        vm.GoalText = "Learn piano";
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Save_NewGoal_PersistsToLocalDb()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.GoalText = "Learn piano";
        vm.Category = "Creative";
        await vm.SaveCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(goals);
        Assert.Equal("Learn piano", goals[0].GoalText);
        Assert.Equal("Creative", goals[0].Category);
    }

    [Fact]
    public async Task Save_NewGoal_NavigatesBack()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.GoalText = "Run 5k";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Contains("..", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task Save_NewGoalWithProgressNote_SavesProgressToo()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.GoalText = "Run 5k";
        vm.NextStepItems = "Start with 1 mile";
        await vm.SaveCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(goals);
        var notes = await GoalProgressRepo.GetForGoalAsync(goals[0].Guid);
        Assert.Single(notes);
        Assert.Equal("Start with 1 mile", notes[0].NextStepItems);
    }

    [Fact]
    public async Task Save_ExistingGoal_UpdatesRecord()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run 5k", EnteredDate = now };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200); // allow Guid setter to trigger LoadAsync
        vm.GoalText = "Run 10k";
        await vm.SaveCommand.ExecuteAsync(null);

        var updated = await GoalRepo.GetAsync(goal.Guid);
        Assert.Equal("Run 10k", updated!.GoalText);
    }

    [Fact]
    public async Task Load_ExistingGoal_PopulatesFields()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run 5k", Category = "Health", EnteredDate = now, UpdatedOn = now };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.Equal("Run 5k", vm.GoalText);
        Assert.Equal("Health", vm.Category);
        Assert.True(vm.IsExisting);
    }

    [Fact]
    public async Task Delete_Confirmed_SoftDeletesGoal()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now };
        await GoalRepo.SaveAsync(goal);

        Nav.AlertConfirmResult = true;
        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);
        await vm.DeleteCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Empty(goals);
    }

    [Fact]
    public async Task Delete_Cancelled_KeepsGoal()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now };
        await GoalRepo.SaveAsync(goal);

        Nav.AlertConfirmResult = false;
        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);
        await vm.DeleteCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(goals);
    }

    [Fact]
    public async Task MarkComplete_SetsCompletionDate()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        vm.GoalText = "Run"; // ensure GoalText is set for alert message
        await Task.Delay(200);
        await vm.MarkCompleteCommand.ExecuteAsync(null);

        var updated = await GoalRepo.GetAsync(goal.Guid);
        Assert.NotNull(updated!.CompletionDate);
    }

    [Fact]
    public async Task AddLinkedTodo_Confirmed_SavesTodoWithGoalPrefix()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run 5k", EnteredDate = now };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = "Sign up for race";
        var vm = BuildVm();
        vm.Guid = goal.Guid;
        vm.GoalText = "Run 5k";
        await vm.AddLinkedTodoCommand.ExecuteAsync(null);

        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(todos);
        Assert.Equal("Sign up for race", todos[0].Title);
        Assert.StartsWith("Goal: Run 5k", todos[0].Notes);
    }

    [Fact]
    public async Task AddLinkedTodo_EmptyGuid_DoesNotSaveTodo()
    {
        var account = await CreateTestAccountAsync();

        Nav.PromptResult = "Some task";
        var vm = BuildVm();
        // Guid is empty — AddLinkedTodoAsync returns early
        vm.GoalText = "Run 5k";
        await vm.AddLinkedTodoCommand.ExecuteAsync(null);

        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Empty(todos);
    }

    [Fact]
    public async Task Save_ExistingGoalWithUnchangedProgressNote_DoesNotDuplicateNote()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run 5k", EnteredDate = now };
        await GoalRepo.SaveAsync(goal);

        // First save adds a progress note
        var progress = new GoalProgress
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            GoalFk = goal.Guid,
            NextStepItems = "Start with 1 mile",
            UpdatedOn = now
        };
        await GoalProgressRepo.SaveAsync(progress);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200); // load existing note into _loadedNextStepItems

        // Save again without changing the note
        await vm.SaveCommand.ExecuteAsync(null);

        var notes = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Single(notes); // should not duplicate
    }
}
