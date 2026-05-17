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
    private int overdueTodoCount;

    [ObservableProperty]
    private bool hasOverdueTodos;

    [ObservableProperty]
    private bool isRefreshing;

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            StatusMessage = string.Empty;
            var account = await accountService.GetAccountAsync();
            if (account is null) return;
            var items = await repo.GetPendingAsync(account.Guid);
            Todos = new ObservableCollection<Todo>(items);
            CompletedTodoCount = await repo.GetCompletedCountAsync(account.Guid);
            HasCompletedTodos = CompletedTodoCount > 0;
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
            Todos = new ObservableCollection<Todo>(items);
            CompletedTodoCount = await repo.GetCompletedCountAsync(account.Guid);
            HasCompletedTodos = CompletedTodoCount > 0;
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
        Todos.Insert(0, todo);
        NewTodoTitle = string.Empty;
    }

    [RelayCommand]
    private async Task CompleteAsync(Todo todo)
    {
        await repo.CompleteAsync(todo.Guid);
        Todos.Remove(todo);
        CompletedTodoCount++;
        HasCompletedTodos = true;
        UpdateOverdueCount(Todos);
    }

    [RelayCommand]
    private async Task DeleteAsync(Todo todo)
    {
        await repo.DeleteAsync(todo.Guid);
        Todos.Remove(todo);
        UpdateOverdueCount(Todos);
    }

    private void UpdateOverdueCount(IEnumerable<Todo> items)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        OverdueTodoCount = items.Count(t => t.DueDate.HasValue && t.DueDate.Value < nowMs);
        HasOverdueTodos = OverdueTodoCount > 0;
    }

    [RelayCommand]
    private async Task OpenAsync(Todo todo) =>
        await Shell.Current.GoToAsync($"todos/entry?guid={todo.Guid}");
}
