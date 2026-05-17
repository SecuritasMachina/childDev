using System.Collections.ObjectModel;
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Linq;

namespace ChildDev.Mobile.ViewModels;

public partial class GoalListViewModel(
    GoalRepository repo,
    GoalProgressRepository progressRepo,
    AccountService accountService,
    SyncService syncService) : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Goal> goals = [];

    [ObservableProperty]
    private string statusMessage = string.Empty;

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
            Goals = new ObservableCollection<Goal>(await LoadGoalsWithStepsAsync(account.Guid));
        }
        catch
        {
            StatusMessage = "Could not load goals. Please restart the app.";
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
            Goals = new ObservableCollection<Goal>(await LoadGoalsWithStepsAsync(account.Guid));
            StatusMessage = string.Empty;
        }
        catch
        {
            StatusMessage = "Could not refresh goals.";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task<List<Goal>> LoadGoalsWithStepsAsync(string accountGuid)
    {
        var goals = await repo.GetAllActiveAsync(accountGuid);
        var steps = await progressRepo.GetLatestNextStepsAsync(accountGuid);
        foreach (var g in goals)
            g.LatestNextStepItems = steps.GetValueOrDefault(g.Guid);
        return goals;
    }

    [RelayCommand]
    private async Task AddAsync() =>
        await Shell.Current.GoToAsync("goals/entry");

    [RelayCommand]
    private async Task OpenAsync(Goal goal) =>
        await Shell.Current.GoToAsync($"goals/entry?guid={goal.Guid}");

    [RelayCommand]
    private async Task DeleteAsync(Goal goal)
    {
        await repo.DeleteAsync(goal.Guid);
        Goals.Remove(goal);
    }
}
