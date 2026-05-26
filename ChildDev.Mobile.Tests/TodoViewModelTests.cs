using LevelUp.Data;
using LevelUp.Models;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

public class TodoViewModelTests : ViewModelTestBase
{
    private TodoListViewModel BuildListVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    private TodoEntryViewModel BuildEntryVm() =>
        new(TodoRepo, GoalRepo, AccountService, Analytics, Nav);

    [Fact]
    public async Task TodoList_Load_PopulatesPendingTodos()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Buy milk", UpdatedOn = now });

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Single(vm.Todos);
    }

    [Fact]
    public async Task TodoList_Load_DoesNotRequireNetwork()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Offline task", UpdatedOn = now });

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Single(vm.Todos);
        Assert.Empty(vm.StatusMessage);
    }

    [Fact]
    public async Task TodoList_Add_InlineTitle_SavesTodo()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.NewTodoTitle = "Practice guitar";
        await vm.AddCommand.ExecuteAsync(null);

        Assert.Single(vm.Todos);
        Assert.Equal("Practice guitar", vm.Todos[0].Title);
        Assert.Empty(vm.NewTodoTitle);
    }

    [Fact]
    public async Task TodoList_Add_EmptyTitle_DoesNotSave()
    {
        await CreateTestAccountAsync();
        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.NewTodoTitle = string.Empty;
        Assert.False(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public async Task TodoList_Complete_MovesToCompleted()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Buy milk", UpdatedOn = now });

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.CompleteCommand.ExecuteAsync(vm.Todos[0]);

        Assert.Empty(vm.Todos);
        Assert.Single(vm.CompletedTodos);
    }

    [Fact]
    public async Task TodoList_Delete_Confirmed_RemovesTodo()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Buy milk", UpdatedOn = now });

        Nav.AlertConfirmResult = true;
        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(vm.Todos[0]);

        Assert.Empty(vm.Todos);
    }

    [Fact]
    public async Task TodoList_Delete_Cancelled_KeepsTodo()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Buy milk", UpdatedOn = now });

        Nav.AlertConfirmResult = false;
        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(vm.Todos[0]);

        Assert.Single(vm.Todos);
    }

    [Fact]
    public async Task TodoList_FilterText_FiltersItems()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Buy milk", UpdatedOn = now });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Write code", UpdatedOn = now });

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "milk";

        Assert.Single(vm.Todos);
    }

    [Fact]
    public async Task TodoList_FilterText_Clear_RestoresAll()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Buy milk", UpdatedOn = now });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Write code", UpdatedOn = now });

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "milk";
        Assert.Single(vm.Todos);

        vm.FilterText = string.Empty;
        Assert.Equal(2, vm.Todos.Count);
    }

    [Fact]
    public void TodoEntry_CanSave_EmptyTitle_ReturnsFalse()
    {
        var vm = BuildEntryVm();
        vm.Title = string.Empty;
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void TodoEntry_CanSave_WithTitle_ReturnsTrue()
    {
        var vm = BuildEntryVm();
        vm.Title = "Do something";
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task TodoEntry_Save_PersistsOffline()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildEntryVm();
        vm.Title = "Learn a song";
        await vm.SaveCommand.ExecuteAsync(null);

        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(todos);
        Assert.Equal("Learn a song", todos[0].Title);
    }

    [Fact]
    public async Task TodoEntry_Save_NavigatesBack()
    {
        await CreateTestAccountAsync();
        var vm = BuildEntryVm();
        vm.Title = "Do laundry";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Contains("..", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task TodoEntry_Load_PopulatesFields()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "My task", Notes = "Some notes", UpdatedOn = now };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildEntryVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        Assert.Equal("My task", vm.Title);
        Assert.Equal("Some notes", vm.Notes);
        Assert.True(vm.IsExisting);
    }

    [Fact]
    public async Task TodoEntry_MarkDone_CompletesTodo()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Task", UpdatedOn = now };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildEntryVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);
        await vm.MarkDoneCommand.ExecuteAsync(null);

        var completed = await TodoRepo.GetCompletedAsync(account.Guid);
        Assert.Single(completed);
        Assert.Contains("..", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task TodoEntry_Restore_UncompletesTodo()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Task", UpdatedOn = now };
        await TodoRepo.SaveAsync(todo);

        // Complete it first
        await TodoRepo.CompleteAsync(todo.Guid);

        var vm = BuildEntryVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);
        await vm.RestoreCommand.ExecuteAsync(null);

        var pending = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.Contains("..", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task TodoEntry_Delete_Confirmed_RemovesTodo()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Delete me", UpdatedOn = now };
        await TodoRepo.SaveAsync(todo);

        Nav.AlertConfirmResult = true;
        var vm = BuildEntryVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);
        await vm.DeleteCommand.ExecuteAsync(null);

        var pending = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Empty(pending);
        Assert.Contains("..", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task TodoEntry_Delete_Cancelled_KeepsTodo()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Keep me", UpdatedOn = now };
        await TodoRepo.SaveAsync(todo);

        Nav.AlertConfirmResult = false;
        var vm = BuildEntryVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);
        await vm.DeleteCommand.ExecuteAsync(null);

        var pending = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(pending);
    }
}
