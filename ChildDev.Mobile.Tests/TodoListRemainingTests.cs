using LevelUp.Data;
using LevelUp.Models;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

/// <summary>
/// Covers UpdateWeekCompletedMessage, UpdateOverdueCount, Refresh, and FilterText branches.
/// </summary>
public class TodoListRemainingTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_WithOverdueTodos_SetsOverdueCount()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Overdue", UpdatedOn = now, DueDate = yesterday });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Normal", UpdatedOn = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.OverdueTodoCount);
        Assert.True(vm.HasOverdueTodos);
        Assert.Contains("overdue", vm.EntryCountDisplay);
    }

    [Fact]
    public async Task Load_WithCompletedThisWeek_SetsWeekMessage()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Done1", UpdatedOn = now, CompletedAt = now });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Done2", UpdatedOn = now, CompletedAt = now });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Done3", UpdatedOn = now, CompletedAt = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasWeekCompletedMessage);
        Assert.NotEmpty(vm.WeekCompletedMessage);
    }

    [Fact]
    public async Task Load_5OrMoreThisWeek_ShowsGreatMomentum()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < 5; i++)
            await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = $"Done{i}", UpdatedOn = now, CompletedAt = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Contains("🌟", vm.WeekCompletedMessage);
    }

    [Fact]
    public async Task Load_10OrMoreThisWeek_ShowsLegendary()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < 10; i++)
            await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = $"Done{i}", UpdatedOn = now, CompletedAt = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Contains("🔥", vm.WeekCompletedMessage);
    }

    [Fact]
    public async Task Load_1CompletedThisWeek_SingularMessage()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "OneDone", UpdatedOn = now, CompletedAt = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Contains("1 todo done", vm.WeekCompletedMessage);
    }

    [Fact]
    public async Task Load_WithLastWeekData_ShowsWeekOverWeek()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var thisWeek = DateTimeOffset.UtcNow.AddDays(-3).ToUnixTimeMilliseconds();
        var lastWeek = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();

        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "ThisWeek1", UpdatedOn = now, CompletedAt = thisWeek });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "ThisWeek2", UpdatedOn = now, CompletedAt = thisWeek });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "LastWeek", UpdatedOn = now, CompletedAt = lastWeek });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.HasWeekOverWeekMessage);
    }

    [Fact]
    public async Task Complete_IncrementsCompletedAndUpdatesOverdue()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Overdue", UpdatedOn = now, DueDate = yesterday });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.OverdueTodoCount);

        await vm.CompleteCommand.ExecuteAsync(vm.Todos[0]);
        Assert.Equal(0, vm.OverdueTodoCount);
        Assert.True(vm.HasCompletedTodos);
    }

    [Fact]
    public async Task Refresh_NoAccount_SetsIsRefreshingFalse()
    {
        var vm = BuildVm();
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.False(vm.IsRefreshing);
    }

    [Fact]
    public async Task Refresh_WithAccount_LoadsTodos()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Task", UpdatedOn = now });

        var vm = BuildVm();
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Single(vm.Todos);
        Assert.False(vm.IsRefreshing);
    }

    [Fact]
    public async Task Delete_CompletedTodo_UpdatesHasCompletedTodos()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Done", UpdatedOn = now, CompletedAt = now });

        Nav.AlertConfirmResult = true;
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.CompletedTodos);
        Assert.True(vm.HasCompletedTodos);

        await vm.DeleteCommand.ExecuteAsync(vm.CompletedTodos[0]);
        Assert.Empty(vm.CompletedTodos);
        Assert.False(vm.HasCompletedTodos);
    }

    [Fact]
    public async Task SnoozeOverdue_WithAccount_SnoozesTodos()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Overdue", UpdatedOn = now, DueDate = yesterday });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.OverdueTodoCount);

        await vm.SnoozeOverdueCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.OverdueTodoCount);
    }

    [Fact]
    public async Task FilterText_NarrowsTodos()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Buy milk", UpdatedOn = now });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Practice piano", UpdatedOn = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Todos.Count);

        vm.FilterText = "piano";
        Assert.Single(vm.Todos);
    }
}
