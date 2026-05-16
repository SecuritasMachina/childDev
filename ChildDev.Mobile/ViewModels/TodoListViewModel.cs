using System.Collections.ObjectModel;
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildDev.Mobile.ViewModels;

public partial class TodoListViewModel(
    TodoRepository repo,
    AccountService accountService) : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Todo> todos = [];

    [ObservableProperty]
    private string newTodoTitle = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;
        var items = await repo.GetPendingAsync(account.Guid);
        Todos = new ObservableCollection<Todo>(items);
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
    }

    [RelayCommand]
    private async Task DeleteAsync(Todo todo)
    {
        await repo.DeleteAsync(todo.Guid);
        Todos.Remove(todo);
    }

    [RelayCommand]
    private async Task OpenAsync(Todo todo) =>
        await Shell.Current.GoToAsync($"todos/entry?guid={todo.Guid}");
}
