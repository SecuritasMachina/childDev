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

    [ObservableProperty]
    private string filterText = string.Empty;

    [ObservableProperty]
    private string entryCountDisplay = string.Empty;

    [ObservableProperty]
    private string emptyMessage = "No goals yet";

    private List<Goal> _allGoals = [];

    partial void OnFilterTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Goals = new ObservableCollection<Goal>(_allGoals);
            EmptyMessage = "No goals yet";
            UpdateEntryCountDisplay();
        }
        else
        {
            var filtered = _allGoals.Where(g =>
                (g.GoalText != null && g.GoalText.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
                (g.MeasurableOutcome != null && g.MeasurableOutcome.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
                (g.LatestNextStepItems != null && g.LatestNextStepItems.Contains(value, StringComparison.OrdinalIgnoreCase))).ToList();
            Goals = new ObservableCollection<Goal>(filtered);
            EmptyMessage = $"No matches for \"{value}\"";
            var n = filtered.Count;
            EntryCountDisplay = $"{n} {(n == 1 ? "goal" : "goals")} matching";
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
            _allGoals = await LoadGoalsWithStepsAsync(account.Guid);
            Goals = new ObservableCollection<Goal>(_allGoals);
            UpdateEntryCountDisplay();
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
            _allGoals = await LoadGoalsWithStepsAsync(account.Guid);
            Goals = new ObservableCollection<Goal>(_allGoals);
            UpdateEntryCountDisplay();
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

    private void UpdateEntryCountDisplay()
    {
        var active = _allGoals.Count(g => g.CompletionDate is null);
        var completed = _allGoals.Count - active;
        EntryCountDisplay = completed > 0
            ? $"{active} active, {completed} completed"
            : $"{active} {(active == 1 ? "goal" : "goals")}";
    }

    private async Task<List<Goal>> LoadGoalsWithStepsAsync(string accountGuid)
    {
        var goals = await repo.GetAllActiveAsync(accountGuid);
        var info = await progressRepo.GetLatestProgressInfoAsync(accountGuid);
        foreach (var g in goals)
        {
            if (info.TryGetValue(g.Guid, out var p))
            {
                g.LatestNextStepItems = p.Steps;
                g.LatestProgressAt = p.UpdatedOn;
            }
        }
        // Goals with no progress first (needs attention), then oldest-update-first
        return goals
            .OrderBy(g => g.LatestProgressAt.HasValue ? 1 : 0)
            .ThenBy(g => g.LatestProgressAt ?? 0)
            .ToList();
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
        await progressRepo.DeleteForGoalAsync(goal.Guid);
        _allGoals.Remove(goal);
        Goals.Remove(goal);
        UpdateEntryCountDisplay();
    }
}
