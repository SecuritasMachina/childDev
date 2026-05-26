using System.Collections.ObjectModel;
using LevelUp.Data;
using LevelUp.Models;
using LevelUp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Linq;

namespace LevelUp.ViewModels;

public partial class GoalListViewModel(
    GoalRepository repo,
    GoalProgressRepository progressRepo,
    AccountService accountService,
    SyncService syncService,
    MobileAnalyticsService analytics,
    INavigationService nav) : ObservableObject
{
    private readonly INavigationService _nav = nav;
    [ObservableProperty]
    private ObservableCollection<Goal> goals = [];

    [ObservableProperty]
    private bool hasGoals;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isRefreshing;

    [ObservableProperty]
    private string filterText = string.Empty;

    [ObservableProperty]
    private string categoryFilter = "All";

    [ObservableProperty]
    private string entryCountDisplay = string.Empty;

    [ObservableProperty]
    private string emptyMessage = "No goals yet";

    private List<Goal> _allGoals = [];

    partial void OnFilterTextChanged(string value) => ApplyFilters();
    partial void OnCategoryFilterChanged(string value) => ApplyFilters();

    [RelayCommand]
    private void SetCategoryFilter(string value) => CategoryFilter = value;

    private void ApplyFilters()
    {
        var textQ = FilterText?.Trim() ?? string.Empty;
        var catQ = string.IsNullOrEmpty(CategoryFilter) || CategoryFilter == "All" ? null : CategoryFilter;
        var needsAttention = catQ == "NeedsAttention";
        var staleThresholdMs = DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeMilliseconds();

        var filtered = _allGoals.Where(g =>
            (string.IsNullOrEmpty(textQ) ||
                (g.GoalText != null && g.GoalText.Contains(textQ, StringComparison.OrdinalIgnoreCase)) ||
                (g.MeasurableOutcome != null && g.MeasurableOutcome.Contains(textQ, StringComparison.OrdinalIgnoreCase)) ||
                (g.LatestNextStepItems != null && g.LatestNextStepItems.Contains(textQ, StringComparison.OrdinalIgnoreCase))) &&
            (!needsAttention || (g.CompletionDate is null &&
                (!g.LatestProgressAt.HasValue || g.LatestProgressAt.Value < staleThresholdMs))) &&
            (needsAttention || catQ == null || g.Category == catQ)
        ).ToList();

        Goals = new ObservableCollection<Goal>(filtered);
        HasGoals = filtered.Count > 0;

        if (!string.IsNullOrEmpty(textQ))
        {
            EmptyMessage = $"No matches for \"{textQ}\"";
            var n = filtered.Count;
            EntryCountDisplay = $"{n} {(n == 1 ? "goal" : "goals")} matching";
        }
        else if (needsAttention)
        {
            EmptyMessage = "All goals are up to date! 🎉";
            EntryCountDisplay = filtered.Count == 0 ? string.Empty : $"{filtered.Count} goal{(filtered.Count == 1 ? "" : "s")} need attention";
        }
        else if (catQ != null)
        {
            EmptyMessage = $"No {catQ} goals";
            EntryCountDisplay = $"{filtered.Count} {catQ.ToLower()} goal{(filtered.Count == 1 ? "" : "s")}";
        }
        else
        {
            EmptyMessage = "No goals yet";
            UpdateEntryCountDisplay();
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
            analytics.Track("goal_list_view");
            _allGoals = await LoadGoalsWithStepsAsync(account.Guid);
            ApplyFilters();
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
            ApplyFilters();
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
                g.ProgressNotesCount = p.Count;
            }
        }
        var active = goals.Where(g => g.CompletionDate is null)
            .OrderByDescending(g => g.IsPinned)
            .ThenBy(g => g.LatestProgressAt.HasValue ? 1 : 0)
            .ThenBy(g => g.LatestProgressAt ?? 0)
            .ToList();
        var completed = goals.Where(g => g.CompletionDate is not null)
            .OrderByDescending(g => g.CompletionDate ?? 0)
            .ToList();
        return [.. active, .. completed];
    }

    [RelayCommand]
    private async Task AddAsync() =>
        await _nav.GoToAsync("goals/entry");

    [RelayCommand]
    private async Task OpenAsync(Goal goal) =>
        await _nav.GoToAsync($"goals/entry?guid={goal.Guid}");

    [RelayCommand]
    private async Task TogglePinAsync(Goal goal)
    {
        goal.IsPinned = !goal.IsPinned;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        goal.UpdatedOn = ts;
        await repo.SaveAsync(goal);
        _allGoals = await LoadGoalsWithStepsAsync((await accountService.GetAccountAsync())!.Guid);
        Goals = new ObservableCollection<Goal>(_allGoals);
    }

    [RelayCommand]
    private async Task DeleteAsync(Goal goal)
    {
        var confirmed = await _nav.DisplayAlertAsync("Delete Goal?", "Remove this goal and all its progress notes?", "Delete", "Cancel");
        if (!confirmed) return;
        await repo.DeleteAsync(goal.Guid);
        await progressRepo.DeleteForGoalAsync(goal.Guid);
        _allGoals.Remove(goal);
        Goals.Remove(goal);
        UpdateEntryCountDisplay();
    }

    [RelayCommand]
    private async Task QuickNoteAsync(Goal goal)
    {
        var note = await _nav.DisplayPromptAsync(
            "📝 Quick Note",
            $"Add a progress note for:\n\"{(goal.GoalText?.Length > 60 ? goal.GoalText[..60] + "…" : goal.GoalText)}\"",
            "Save", "Cancel",
            "What progress did you make?",
            500);
        if (string.IsNullOrWhiteSpace(note)) return;
        var account = await accountService.GetAccountAsync();
        if (account is null) return;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await progressRepo.SaveAsync(new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            GoalFk = goal.Guid,
            NextStepItems = note.Trim(),
            UpdatedOn = ts
        });
        analytics.Track("goal_quick_note", goal.Category);
        _allGoals = await LoadGoalsWithStepsAsync(account.Guid);
        Goals = new ObservableCollection<Goal>(_allGoals);
        await _nav.AlertAsync("✅ Note Saved!", "Great work keeping your goal moving forward! 🌟", "OK");
    }
}
