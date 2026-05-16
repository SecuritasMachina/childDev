using System.Collections.ObjectModel;
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildDev.Mobile.ViewModels;

public partial class DashboardViewModel(
    JournalRepository journalRepo,
    GoalRepository goalRepo,
    TodoRepository todoRepo,
    AccountService accountService,
    SyncService syncService) : ObservableObject
{
    [ObservableProperty] private ObservableCollection<Journal> recentJournals = [];
    [ObservableProperty] private int activeGoalCount;
    [ObservableProperty] private int pendingTodoCount;
    [ObservableProperty] private string syncStatus = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;

        await RefreshDataAsync(account);
        _ = RunSyncAsync(account);
    }

    private async Task RefreshDataAsync(Account account)
    {
        var journals = await journalRepo.GetAllActiveAsync(account.Guid);
        RecentJournals = new ObservableCollection<Journal>(journals.Take(3));

        var goals = await goalRepo.GetAllActiveAsync(account.Guid);
        ActiveGoalCount = goals.Count(g => g.CompletionDate is null);

        var todos = await todoRepo.GetPendingAsync(account.Guid);
        PendingTodoCount = todos.Count;
    }

    private async Task RunSyncAsync(Account account)
    {
        SyncStatus = "Syncing...";
        var result = await syncService.RunAsync(account);
        SyncStatus = result switch
        {
            SyncResult.Success => $"Synced {DateTime.Now:t}",
            SyncResult.NoServer => string.Empty,
            SyncResult.Failed => "Sync failed — will retry next open",
            _ => string.Empty
        };
        if (result == SyncResult.Success)
            await RefreshDataAsync(account);
    }

    [RelayCommand]
    private async Task AddJournalAsync() =>
        await Shell.Current.GoToAsync("journal/entry");

    [RelayCommand]
    private async Task GoToSettingsAsync() =>
        await Shell.Current.GoToAsync("settings");

    [RelayCommand]
    private async Task GoToGoalsAsync() =>
        await Shell.Current.GoToAsync("//goals");

    [RelayCommand]
    private async Task GoToTodosAsync() =>
        await Shell.Current.GoToAsync("//todos");
}
