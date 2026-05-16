using System.Collections.ObjectModel;
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildDev.Mobile.ViewModels;

public partial class GoalListViewModel(
    GoalRepository repo,
    AccountService accountService) : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Goal> goals = [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;
        var items = await repo.GetAllActiveAsync(account.Guid);
        Goals = new ObservableCollection<Goal>(items);
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
