using System.Collections.ObjectModel;
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildDev.Mobile.ViewModels;

public partial class TodoListViewModel(
    TodoRepository repo,
    AccountService accountService,
    SyncService syncService) : ObservableObject
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

    private List<Todo> _allTodos = [];

    partial void OnFilterTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Todos = new ObservableCollection<Todo>(_allTodos);
            EmptyMessage = "All done!";
            UpdateOverdueCount(_allTodos);
        }
        else
        {
            var filtered = _allTodos.Where(t =>
                t.Title != null && t.Title.Contains(value, StringComparison.OrdinalIgnoreCase)).ToList();
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
            var items = await repo.GetPendingAsync(account.Guid);
            _allTodos = items;
            Todos = new ObservableCollection<Todo>(items);
            var completed = await repo.GetCompletedAsync(account.Guid);
            CompletedTodoCount = completed.Count;
            HasCompletedTodos = CompletedTodoCount > 0;
            CompletedTodos = new ObservableCollection<Todo>(completed);
            UpdateOverdueCount(items);
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
            await syncService.RunAsync(account);
            var items = await repo.GetPendingAsync(account.Guid);
            _allTodos = items;
            Todos = new ObservableCollection<Todo>(items);
            var completed = await repo.GetCompletedAsync(account.Guid);
            CompletedTodoCount = completed.Count;
            HasCompletedTodos = CompletedTodoCount > 0;
            CompletedTodos = new ObservableCollection<Todo>(completed);
            UpdateOverdueCount(items);
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

    [RelayCommand]
    private async Task AddAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTodoTitle)) return;
        var account = await accountService.GetAccountAsync();
        if (account is null) return;

        var todo = new Todo
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            Title = NewTodoTitle.Trim(),
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await repo.SaveAsync(todo);
        _allTodos.Insert(0, todo);
        if (string.IsNullOrWhiteSpace(FilterText) ||
            (todo.Title?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ?? false))
            Todos.Insert(0, todo);
        NewTodoTitle = string.Empty;
        UpdateOverdueCount(_allTodos);
    }

    [RelayCommand]
    private async Task CompleteAsync(Todo todo)
    {
        await repo.CompleteAsync(todo.Guid);
        _allTodos.Remove(todo);
        Todos.Remove(todo);
        var refreshed = await repo.GetCompletedAsync((await accountService.GetAccountAsync())!.Guid);
        CompletedTodoCount = refreshed.Count;
        HasCompletedTodos = CompletedTodoCount > 0;
        CompletedTodos = new ObservableCollection<Todo>(refreshed);
        UpdateOverdueCount(_allTodos);
    }

    [RelayCommand]
    private async Task UncompleteAsync(Todo todo)
    {
        await repo.UncompleteAsync(todo.Guid);
        CompletedTodos.Remove(todo);
        CompletedTodoCount = CompletedTodos.Count;
        HasCompletedTodos = CompletedTodoCount > 0;
        if (CompletedTodoCount == 0) ShowCompletedTodos = false;
        var pending = await repo.GetPendingAsync((await accountService.GetAccountAsync())!.Guid);
        _allTodos = pending;
        Todos = new ObservableCollection<Todo>(pending);
        UpdateOverdueCount(_allTodos);
    }

    [RelayCommand]
    private void ToggleCompleted() => ShowCompletedTodos = !ShowCompletedTodos;

    [RelayCommand]
    private async Task DeleteAsync(Todo todo)
    {
        await repo.DeleteAsync(todo.Guid);
        _allTodos.Remove(todo);
        Todos.Remove(todo);
        CompletedTodos.Remove(todo);
        CompletedTodoCount = CompletedTodos.Count;
        HasCompletedTodos = CompletedTodoCount > 0;
        if (CompletedTodoCount == 0) ShowCompletedTodos = false;
        UpdateOverdueCount(_allTodos);
    }

    private void UpdateOverdueCount(IEnumerable<Todo> items)
    {
        var list = items as IList<Todo> ?? items.ToList();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        OverdueTodoCount = list.Count(t => t.DueDate.HasValue && t.DueDate.Value < nowMs);
        HasOverdueTodos = OverdueTodoCount > 0;
        EntryCountDisplay = OverdueTodoCount > 0
            ? $"{list.Count} pending, {OverdueTodoCount} overdue"
            : $"{list.Count} {(list.Count == 1 ? "task" : "tasks")} pending";
    }

    [RelayCommand]
    private async Task OpenAsync(Todo todo) =>
        await Shell.Current.GoToAsync($"todos/entry?guid={todo.Guid}");
}
