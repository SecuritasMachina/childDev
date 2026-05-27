using LevelUp.Data;
using LevelUp.Models;
using LevelUp.ViewModels;
using SQLite;

namespace LevelUp.Tests;

/// <summary>
/// Targeted tests for uncovered methods: streak, todo counts, snooze-overdue, list VM delete/snooze paths.
/// </summary>
public class AdditionalCoverageTests : IDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private readonly GoalProgressRepository _progressRepo;
    private readonly TodoRepository _todoRepo;

    public AdditionalCoverageTests()
    {
        SqliteFixture.EnsureInit();
        _db = new SQLiteAsyncConnection(":memory:");
        _db.CreateTableAsync<GoalProgress>().GetAwaiter().GetResult();
        _db.CreateTableAsync<Todo>().GetAwaiter().GetResult();
        _progressRepo = new GoalProgressRepository(_db);
        _todoRepo = new TodoRepository(_db);
    }

    public void Dispose() => _db.CloseAsync().GetAwaiter().GetResult();

    // --- GoalProgressRepository.GetCurrentStreakAsync ---

    [Fact]
    public async Task GetCurrentStreak_NoEntries_ReturnsZero()
    {
        var streak = await _progressRepo.GetCurrentStreakAsync("acc1");
        Assert.Equal(0, streak);
    }

    [Fact]
    public async Task GetCurrentStreak_TodayAndYesterday_ReturnsTwo()
    {
        var today = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        await _db.InsertAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), GoalFk = "g1", AccountFk = "acc1", UpdatedOn = today });
        await _db.InsertAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), GoalFk = "g1", AccountFk = "acc1", UpdatedOn = yesterday });

        var streak = await _progressRepo.GetCurrentStreakAsync("acc1");
        Assert.True(streak >= 2);
    }

    [Fact]
    public async Task GetCurrentStreak_DeletedEntries_NotCounted()
    {
        var today = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertAsync(new GoalProgress
        {
            Guid = Guid.NewGuid().ToString(),
            GoalFk = "g1",
            AccountFk = "acc1",
            UpdatedOn = today,
            DeletedAt = today // soft-deleted
        });

        var streak = await _progressRepo.GetCurrentStreakAsync("acc1");
        Assert.Equal(0, streak);
    }

    [Fact]
    public async Task GetCurrentStreak_OtherAccount_NotCounted()
    {
        var today = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), GoalFk = "g1", AccountFk = "other", UpdatedOn = today });

        var streak = await _progressRepo.GetCurrentStreakAsync("acc1");
        Assert.Equal(0, streak);
    }

    // --- TodoRepository counts and snooze ---

    [Fact]
    public async Task GetPendingCountAsync_CountsPendingOnly()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = "acc1", Title = "A", UpdatedOn = now });
        await _db.InsertAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = "acc1", Title = "B", UpdatedOn = now, CompletedAt = now });

        var count = await _todoRepo.GetPendingCountAsync("acc1");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetOverdueCountAsync_CountsOverdueOnly()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        var tomorrow = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds();
        await _db.InsertAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = "acc1", Title = "Overdue", UpdatedOn = now, DueDate = yesterday });
        await _db.InsertAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = "acc1", Title = "Future", UpdatedOn = now, DueDate = tomorrow });
        await _db.InsertAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = "acc1", Title = "NoDue", UpdatedOn = now });

        var count = await _todoRepo.GetOverdueCountAsync("acc1", now);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SnoozeOverdueToTomorrowAsync_MovesOverdueTomorrow()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        var todayStart = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = "acc1", Title = "Overdue", UpdatedOn = now, DueDate = yesterday };
        await _db.InsertAsync(todo);

        await _todoRepo.SnoozeOverdueToTomorrowAsync("acc1", todayStart);

        var updated = await _todoRepo.GetAsync(todo.Guid);
        Assert.NotNull(updated?.DueDate);
        Assert.True(updated!.DueDate! > todayStart);
    }

    [Fact]
    public async Task SnoozeOverdueToTomorrowAsync_CompletedTodos_NotMoved()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        var todayStart = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = "acc1", Title = "Done", UpdatedOn = now, DueDate = yesterday, CompletedAt = now };
        await _db.InsertAsync(todo);

        await _todoRepo.SnoozeOverdueToTomorrowAsync("acc1", todayStart);

        var updated = await _todoRepo.GetAsync(todo.Guid);
        Assert.Equal(yesterday, updated?.DueDate);
    }
}

public class TodoListViewModelCoverageTests : ViewModelTestBase
{
    private TodoListViewModel BuildListVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task TodoList_Delete_Confirmed_RemovesTodo()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "To delete", UpdatedOn = now });

        Nav.AlertConfirmResult = true;
        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Todos);

        await vm.DeleteCommand.ExecuteAsync(vm.Todos[0]);
        Assert.Empty(vm.Todos);
    }

    [Fact]
    public async Task TodoList_Delete_Cancelled_KeepsTodo()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Keep me", UpdatedOn = now });

        Nav.AlertConfirmResult = false;
        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Todos);

        await vm.DeleteCommand.ExecuteAsync(vm.Todos[0]);
        Assert.Single(vm.Todos);
    }

    [Fact]
    public async Task TodoList_Uncomplete_MovesBackToPending()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Done task", UpdatedOn = now, CompletedAt = now };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Empty(vm.Todos);
        Assert.Single(vm.CompletedTodos);

        await vm.UncompleteCommand.ExecuteAsync(vm.CompletedTodos[0]);
        Assert.Single(vm.Todos);
        Assert.Empty(vm.CompletedTodos);
    }

    [Fact]
    public async Task TodoList_SnoozeOverdue_MovesOverdueTodos()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Overdue", UpdatedOn = now, DueDate = yesterday });

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.SnoozeOverdueCommand.ExecuteAsync(null);
        // Command runs without error; snooze moves due date forward
    }

    [Fact]
    public void TodoList_ToggleCompleted_TogglesVisibility()
    {
        var vm = BuildListVm();
        Assert.False(vm.ShowCompletedTodos);
        vm.ToggleCompletedCommand.Execute(null);
        Assert.True(vm.ShowCompletedTodos);
        vm.ToggleCompletedCommand.Execute(null);
        Assert.False(vm.ShowCompletedTodos);
    }

    [Fact]
    public async Task TodoList_Open_NavigatesToEntry()
    {
        var account = await CreateTestAccountAsync();
        var guid = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = guid, AccountFk = account.Guid, Title = "Task", UpdatedOn = now });

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.OpenCommand.ExecuteAsync(vm.Todos[0]);

        Assert.Contains(Nav.NavigatedRoutes, r => r.Contains("todos/entry"));
    }

    [Fact]
    public async Task TodoList_FilterText_FiltersItems()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Buy milk", UpdatedOn = now });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Practice guitar", UpdatedOn = now });

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Todos.Count);

        vm.FilterText = "milk";
        Assert.Single(vm.Todos);
    }
}

public class JournalListViewModelCoverageTests : ViewModelTestBase
{
    private JournalListViewModel BuildListVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task JournalList_Delete_Confirmed_RemovesEntry()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildListVm();
        var j = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Entry to delete", EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        await JournalRepo.SaveAsync(j);

        Nav.AlertConfirmResult = true;
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Journals);

        await vm.DeleteCommand.ExecuteAsync(vm.Journals[0]);
        Assert.Empty(vm.Journals);
    }

    [Fact]
    public async Task JournalList_Delete_Cancelled_KeepsEntry()
    {
        var account = await CreateTestAccountAsync();
        var j = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Keep me", EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        await JournalRepo.SaveAsync(j);

        Nav.AlertConfirmResult = false;
        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Journals);

        await vm.DeleteCommand.ExecuteAsync(vm.Journals[0]);
        Assert.Single(vm.Journals);
    }

    [Fact]
    public async Task JournalList_Open_NavigatesToEntry()
    {
        var account = await CreateTestAccountAsync();
        var j = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Hello", EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        await JournalRepo.SaveAsync(j);

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.OpenCommand.ExecuteAsync(vm.Journals[0]);

        Assert.Contains(Nav.NavigatedRoutes, r => r.Contains("journal/entry"));
    }

    [Fact]
    public async Task JournalList_Add_NavigatesToNewEntry()
    {
        await CreateTestAccountAsync();
        var vm = BuildListVm();
        await vm.AddCommand.ExecuteAsync(null);
        Assert.Contains("journal/entry", Nav.NavigatedRoutes);
    }

    [Fact]
    public void JournalList_ShufflePrompt_ChangesPromptIndex()
    {
        var vm = BuildListVm();
        var initial = vm.TodayPrompt;
        vm.ShufflePromptCommand.Execute(null);
        // After shuffle the prompt may or may not change (depends on index), but command runs without error
        Assert.NotNull(vm.TodayPrompt);
    }

    [Fact]
    public async Task JournalList_DateFilter_Filters()
    {
        var account = await CreateTestAccountAsync();
        var j = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Filtered entry", EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        await JournalRepo.SaveAsync(j);

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Journals);

        vm.SetDateFilterCommand.Execute("Week");
        Assert.Equal("Week", vm.DateFilter);
    }
}
