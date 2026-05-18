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
    GoalProgressRepository progressRepo,
    TodoRepository todoRepo,
    AccountService accountService,
    SyncService syncService) : ObservableObject
{
    [ObservableProperty] private string greeting = string.Empty;
    [ObservableProperty] private ObservableCollection<Journal> recentJournals = [];
    [ObservableProperty] private int activeGoalCount;
    [ObservableProperty] private bool hasNoActiveGoals;
    [ObservableProperty] private int pendingTodoCount;
    [ObservableProperty] private bool hasNoPendingTodos;
    [ObservableProperty] private int overdueTodoCount;
    [ObservableProperty] private bool hasOverdueTodos;
    [ObservableProperty] private int journalThisWeek;
    [ObservableProperty] private string nextGoalMeeting = string.Empty;
    [ObservableProperty] private bool hasNextGoalMeeting;
    [ObservableProperty] private string staleGoalText = string.Empty;
    [ObservableProperty] private bool hasStaleGoal;
    [ObservableProperty] private string staleGoalGuid = string.Empty;
    [ObservableProperty] private string syncStatus = string.Empty;
    [ObservableProperty] private string lastSyncDisplay = string.Empty;
    [ObservableProperty] private string quickJournalText = string.Empty;
    [ObservableProperty] private bool quickJournalSaved;

    private string _accountGuid = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var account = await accountService.GetAccountAsync();
            if (account is null) return;
            _accountGuid = account.Guid;

            LastSyncDisplay = account.LastSyncAt == 0
                ? "Never synced"
                : $"Last synced: {DateTimeOffset.FromUnixTimeMilliseconds(account.LastSyncAt).LocalDateTime:g}";

            var hour = DateTime.Now.Hour;
            var timeOfDay = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
            Greeting = $"{timeOfDay}, {account.NickName}!";

            await RefreshDataAsync(account);
            _ = RunSyncAsync(account);
        }
        catch
        {
            SyncStatus = "Could not load dashboard data.";
        }
    }

    private async Task RefreshDataAsync(Account account)
    {
        var journals = await journalRepo.GetRecentAsync(account.Guid, 3);
        RecentJournals = new ObservableCollection<Journal>(journals);

        var weekStartMs = DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeMilliseconds();
        JournalThisWeek = await journalRepo.GetCountSinceAsync(account.Guid, weekStartMs);

        var goals = await goalRepo.GetAllActiveAsync(account.Guid);
        var activeGoals = goals.Where(g => g.CompletionDate is null).ToList();
        ActiveGoalCount = activeGoals.Count;
        HasNoActiveGoals = activeGoals.Count == 0;
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todayStartMs = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        var nextGoal = activeGoals
            .Where(g => g.NextMeetingDate.HasValue && g.NextMeetingDate.Value >= todayStartMs)
            .OrderBy(g => g.NextMeetingDate!.Value)
            .FirstOrDefault();
        if (nextGoal is not null)
        {
            var meetingDate = DateTimeOffset.FromUnixTimeMilliseconds(nextGoal.NextMeetingDate!.Value).LocalDateTime;
            var daysAway = (meetingDate.Date - DateTime.Today).Days;
            var dateStr = daysAway == 0 ? "today" : daysAway == 1 ? "tomorrow" : meetingDate.ToString("MMM d");
            NextGoalMeeting = $"Next goal meeting: {dateStr}";
            HasNextGoalMeeting = true;
        }
        else
        {
            NextGoalMeeting = string.Empty;
            HasNextGoalMeeting = false;
        }

        var todos = await todoRepo.GetPendingAsync(account.Guid);
        PendingTodoCount = todos.Count;
        HasNoPendingTodos = todos.Count == 0;
        OverdueTodoCount = todos.Count(t => t.DueDate.HasValue && t.DueDate.Value < todayStartMs);
        HasOverdueTodos = OverdueTodoCount > 0;

        // Find the active goal with no progress or oldest progress
        var progressInfo = await progressRepo.GetLatestProgressInfoAsync(account.Guid);
        var staleGoal = activeGoals
            .OrderBy(g => progressInfo.ContainsKey(g.Guid) ? 1 : 0)
            .ThenBy(g => progressInfo.TryGetValue(g.Guid, out var p) ? p.UpdatedOn : 0)
            .FirstOrDefault();
        if (staleGoal is not null && (!progressInfo.ContainsKey(staleGoal.Guid)
            || (nowMs - progressInfo[staleGoal.Guid].UpdatedOn) > 7L * 86_400_000))
        {
            StaleGoalText = staleGoal.GoalText ?? string.Empty;
            StaleGoalGuid = staleGoal.Guid;
            HasStaleGoal = !string.IsNullOrWhiteSpace(StaleGoalText);
        }
        else
        {
            StaleGoalText = string.Empty;
            StaleGoalGuid = string.Empty;
            HasStaleGoal = false;
        }
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
        {
            LastSyncDisplay = $"Last synced: {DateTime.Now:g}";
            try { await RefreshDataAsync(account); }
            catch { SyncStatus = "Sync OK but dashboard refresh failed."; }
        }
    }

    [RelayCommand]
    private async Task QuickAddJournalAsync()
    {
        var text = QuickJournalText?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        if (string.IsNullOrEmpty(_accountGuid)) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await journalRepo.SaveAsync(new Journal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = _accountGuid,
            Notes = text,
            EnteredDate = now,
            UpdatedOn = now
        });
        QuickJournalText = string.Empty;
        QuickJournalSaved = true;
        await Task.Delay(1500);
        QuickJournalSaved = false;
        var journals = await journalRepo.GetRecentAsync(_accountGuid, 3);
        RecentJournals = new ObservableCollection<Journal>(journals);
    }

    [RelayCommand]
    private async Task GoToStaleGoalAsync()
    {
        if (!string.IsNullOrEmpty(StaleGoalGuid))
            await Shell.Current.GoToAsync($"goals/entry?guid={StaleGoalGuid}");
    }

    [RelayCommand]
    private async Task AddJournalAsync() =>
        await Shell.Current.GoToAsync("journal/entry");

    [RelayCommand]
    private async Task OpenJournalAsync(Journal journal) =>
        await Shell.Current.GoToAsync($"journal/entry?guid={journal.Guid}");

    [RelayCommand]
    private async Task GoToSettingsAsync() =>
        await Shell.Current.GoToAsync("settings");

    [RelayCommand]
    private async Task GoToGoalsAsync() =>
        await Shell.Current.GoToAsync("//goals");

    [RelayCommand]
    private async Task GoToTodosAsync() =>
        await Shell.Current.GoToAsync("//todos");

    [RelayCommand]
    private async Task GoToJournalAsync() =>
        await Shell.Current.GoToAsync("//journal");
}
