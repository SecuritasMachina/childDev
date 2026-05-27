using System.Collections.ObjectModel;
using LevelUp.Data;
using LevelUp.Models;
using LevelUp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LevelUp.ViewModels;

public partial class TodoListViewModel(
    TodoRepository repo,
    AccountService accountService,
    SyncService syncService,
    MobileAnalyticsService analytics,
    INavigationService nav) : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Todo> todos = [];

    [ObservableProperty]
    private string newTodoTitle = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private int completedTodoCount;

    [ObservableProperty]
    private bool hasCompletedTodos;

    [ObservableProperty]
    private ObservableCollection<Todo> completedTodos = [];

    [ObservableProperty]
    private bool showCompletedTodos;

    [ObservableProperty]
    private int overdueTodoCount;

    [ObservableProperty]
    private bool hasOverdueTodos;

    [ObservableProperty]
    private bool isRefreshing;

    [ObservableProperty]
    private string filterText = string.Empty;

    [ObservableProperty]
    private string entryCountDisplay = string.Empty;

    [ObservableProperty]
    private string emptyMessage = "All done!";

    [ObservableProperty]
    private string weekCompletedMessage = string.Empty;

    [ObservableProperty]
    private bool hasWeekCompletedMessage;

    [ObservableProperty]
    private string weekOverWeekMessage = string.Empty;

    [ObservableProperty]
    private bool hasWeekOverWeekMessage;

    private readonly INavigationService _nav = nav;
    private List<Todo> _allTodos = [];
    private string _accountGuid = string.Empty;

    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewTodoTitle);

    partial void OnNewTodoTitleChanged(string value) => AddCommand.NotifyCanExecuteChanged();

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var value = FilterText;
        if (string.IsNullOrWhiteSpace(value))
        {
            Todos = new ObservableCollection<Todo>(_allTodos);
            EmptyMessage = "All done!";
            UpdateOverdueCount(_allTodos);
        }
        else
        {
            var filtered = _allTodos.Where(t =>
                (t.Title?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.Notes?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
            Todos = new ObservableCollection<Todo>(filtered);
            EmptyMessage = $"No matches for \"{value}\"";
            var n = filtered.Count;
            EntryCountDisplay = $"{n} {(n == 1 ? "task" : "tasks")} matching";
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            StatusMessage = string.Empty;
            var account = await accountService.GetAccountAsync();
            if (account is null) return;
            analytics.Track("todo_list_view");
            _accountGuid = account.Guid;
            var items = await repo.GetPendingAsync(_accountGuid);
            _allTodos = items;
            Todos = new ObservableCollection<Todo>(items);
            var completed = await repo.GetCompletedAsync(_accountGuid);
            CompletedTodoCount = completed.Count;
            HasCompletedTodos = CompletedTodoCount > 0;
            CompletedTodos = new ObservableCollection<Todo>(completed);
            UpdateOverdueCount(items);
            UpdateWeekCompletedMessage(completed);
        }
        catch
        {
            StatusMessage = "Could not load tasks. Please restart the app.";
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            var account = await accountService.GetAccountAsync();
            if (account is null) { IsRefreshing = false; return; }
            _accountGuid = account.Guid;
            await syncService.RunAsync(account);
            var items = await repo.GetPendingAsync(_accountGuid);
            _allTodos = items;
            ApplyFilter();
            var completed = await repo.GetCompletedAsync(_accountGuid);
            CompletedTodoCount = completed.Count;
            HasCompletedTodos = CompletedTodoCount > 0;
            CompletedTodos = new ObservableCollection<Todo>(completed);
            UpdateWeekCompletedMessage(completed);
            StatusMessage = string.Empty;
        }
        catch
        {
            StatusMessage = "Could not refresh tasks.";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTodoTitle)) return;
        if (string.IsNullOrEmpty(_accountGuid))
        {
            var account = await accountService.GetAccountAsync();
            if (account is null) return;
            _accountGuid = account.Guid;
        }

        var todo = new Todo
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = _accountGuid,
            Title = NewTodoTitle.Trim(),
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await repo.SaveAsync(todo);
        analytics.Track("todo_add");
        _allTodos.Insert(0, todo);
        if (string.IsNullOrWhiteSpace(FilterText) ||
            (todo.Title?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (todo.Notes?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ?? false))
            Todos.Insert(0, todo);
        NewTodoTitle = string.Empty;
        UpdateOverdueCount(_allTodos);
    }

    [RelayCommand]
    private async Task CompleteAsync(Todo todo)
    {
        if (todo is null) return;
        await repo.CompleteAsync(todo.Guid);
        analytics.Track("todo_complete");
        _allTodos.Remove(todo);
        Todos.Remove(todo);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        todo.CompletedAt = now;
        todo.UpdatedOn = now;
        CompletedTodos.Insert(0, todo);
        CompletedTodoCount = CompletedTodos.Count;
        HasCompletedTodos = true;
        UpdateOverdueCount(_allTodos);
        UpdateWeekCompletedMessage([.. CompletedTodos]);
    }

    [RelayCommand]
    private async Task UncompleteAsync(Todo todo)
    {
        if (todo is null) return;
        await repo.UncompleteAsync(todo.Guid);
        CompletedTodos.Remove(todo);
        CompletedTodoCount = CompletedTodos.Count;
        HasCompletedTodos = CompletedTodoCount > 0;
        if (CompletedTodoCount == 0) ShowCompletedTodos = false;
        var pending = await repo.GetPendingAsync(_accountGuid);
        _allTodos = pending;
        ApplyFilter();
    }

    [RelayCommand]
    private void ToggleCompleted() => ShowCompletedTodos = !ShowCompletedTodos;

    [RelayCommand]
    private async Task DeleteAsync(Todo todo)
    {
        if (todo is null) return;
        var confirmed = await _nav.DisplayAlertAsync("Delete Todo?", "Remove this todo?", "Delete", "Cancel");
        if (!confirmed) return;
        await repo.DeleteAsync(todo.Guid);
        _allTodos.Remove(todo);
        Todos.Remove(todo);
        CompletedTodos.Remove(todo);
        CompletedTodoCount = CompletedTodos.Count;
        HasCompletedTodos = CompletedTodoCount > 0;
        if (CompletedTodoCount == 0) ShowCompletedTodos = false;
        UpdateOverdueCount(_allTodos);
    }

    [RelayCommand]
    private async Task SnoozeOverdueAsync()
    {
        if (string.IsNullOrEmpty(_accountGuid)) return;
        var todayStartMs = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        await repo.SnoozeOverdueToTomorrowAsync(_accountGuid, todayStartMs);
        analytics.Track("todo_snooze_overdue");
        var items = await repo.GetPendingAsync(_accountGuid);
        _allTodos = items;
        ApplyFilter();
    }

    private void UpdateWeekCompletedMessage(IList<Todo> completed)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var weekStartMs = DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeMilliseconds();
        var lastWeekStartMs = DateTimeOffset.UtcNow.AddDays(-14).ToUnixTimeMilliseconds();
        var weekCount = completed.Count(t => t.CompletedAt.HasValue && t.CompletedAt.Value >= weekStartMs);
        var lastWeekCount = completed.Count(t => t.CompletedAt.HasValue && t.CompletedAt.Value >= lastWeekStartMs && t.CompletedAt.Value < weekStartMs);
        if (weekCount == 0) { WeekCompletedMessage = string.Empty; HasWeekCompletedMessage = false; WeekOverWeekMessage = string.Empty; HasWeekOverWeekMessage = false; return; }
        WeekCompletedMessage = weekCount switch
        {
            >= 10 => $"🔥 {weekCount} todos crushed this week — legendary!",
            >= 5  => $"🌟 {weekCount} todos done this week — great momentum!",
            >= 3  => $"💪 {weekCount} todos completed this week — keep it up!",
            _     => $"✅ {weekCount} todo{(weekCount == 1 ? "" : "s")} done this week!"
        };
        HasWeekCompletedMessage = true;
        if (lastWeekCount > 0)
        {
            var diff = weekCount - lastWeekCount;
            WeekOverWeekMessage = diff > 0 ? $"📈 +{diff} vs last week ({lastWeekCount})"
                : diff < 0 ? $"📉 {Math.Abs(diff)} fewer than last week ({lastWeekCount})"
                : $"📊 Same pace as last week!";
            HasWeekOverWeekMessage = true;
        }
        else
        {
            WeekOverWeekMessage = string.Empty;
            HasWeekOverWeekMessage = false;
        }
    }

    private void UpdateOverdueCount(IEnumerable<Todo> items)
    {
        var list = items as IList<Todo> ?? items.ToList();
        var todayStartMs = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        OverdueTodoCount = list.Count(t => t.DueDate.HasValue && t.DueDate.Value < todayStartMs);
        HasOverdueTodos = OverdueTodoCount > 0;
        EntryCountDisplay = OverdueTodoCount > 0
            ? $"{list.Count} pending, {OverdueTodoCount} overdue"
            : $"{list.Count} {(list.Count == 1 ? "task" : "tasks")} pending";
    }

    [RelayCommand]
    private async Task OpenAsync(Todo todo)
    {
        if (todo is null) return;
        await _nav.GoToAsync($"todos/entry?guid={todo.Guid}");
    }
}
