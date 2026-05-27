using System.Collections.ObjectModel;
using LevelUp.Data;
using LevelUp.Models;
using LevelUp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LevelUp.ViewModels;

public partial class DashboardViewModel(
    JournalRepository journalRepo,
    GoalRepository goalRepo,
    GoalProgressRepository progressRepo,
    TodoRepository todoRepo,
    AccountService accountService,
    SyncService syncService,
    MobileAnalyticsService analytics,
    INavigationService nav) : ObservableObject
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
    [ObservableProperty] private int weekTodosCompleted;
    [ObservableProperty] private int weekProgressNotes;
    [ObservableProperty] private int weekJournalEntries;
    [ObservableProperty] private bool hasWeeklyWins;
    [ObservableProperty] private string overallTierLabel = string.Empty;
    [ObservableProperty] private int totalProgressNotes;
    [ObservableProperty] private string weeklyChallengeTitle = string.Empty;
    [ObservableProperty] private string weeklyChallengeDesc = string.Empty;
    [ObservableProperty] private string weeklyChallengeStatus = string.Empty;
    [ObservableProperty] private string weeklyChallengeMotivation = string.Empty;
    [ObservableProperty] private double weeklyChallengePctValue;
    [ObservableProperty] private bool weeklyChallengeDone;
    [ObservableProperty] private bool hasWeeklyChallenge;
    [ObservableProperty] private string streakDisplay = string.Empty;

    private readonly INavigationService _nav = nav;
    private string _accountGuid = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var account = await accountService.GetAccountAsync();
            if (account is null) return;
            _accountGuid = account.Guid;
            analytics.Track("dashboard_view");

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
        WeekJournalEntries = JournalThisWeek;

        var completedTodos = await todoRepo.GetCompletedAsync(account.Guid);
        WeekTodosCompleted = completedTodos.Count(t => t.CompletedAt >= weekStartMs);

        var recentProgress = await progressRepo.GetModifiedSinceAsync(account.Guid, weekStartMs);
        WeekProgressNotes = recentProgress.Count(p => p.DeletedAt == null);

        HasWeeklyWins = WeekTodosCompleted > 0 || WeekProgressNotes > 0 || WeekJournalEntries > 0;

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

        PendingTodoCount = await todoRepo.GetPendingCountAsync(account.Guid);
        HasNoPendingTodos = PendingTodoCount == 0;
        OverdueTodoCount = await todoRepo.GetOverdueCountAsync(account.Guid, todayStartMs);
        HasOverdueTodos = OverdueTodoCount > 0;

        // Find the active goal with no progress or oldest progress
        var progressInfo = await progressRepo.GetLatestProgressInfoAsync(account.Guid);
        TotalProgressNotes = progressInfo.Values.Sum(p => p.Count);
        OverallTierLabel = TotalProgressNotes switch
        {
            >= 500 => "🌟 Legend",
            >= 200 => "🏆 Master",
            >= 100 => "💎 Expert",
            >= 50  => "⭐ Skilled",
            >= 20  => "🚀 Apprentice",
            >= 5   => "🌱 Beginner",
            _      => string.Empty
        };
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

        var streak = await progressRepo.GetCurrentStreakAsync(account.Guid);
        StreakDisplay = streak >= 2
            ? $"{(streak >= 14 ? "🌟" : streak >= 7 ? "🔥" : "⚡")} {streak}-day streak!"
            : string.Empty;

        // Weekly challenge — rotates every week
        if (activeGoals.Count > 0)
        {
            var wcWeek = System.Globalization.ISOWeek.GetWeekOfYear(DateTime.Today);
            int wcTarget, wcCurrent;
            string wcEmoji, wcTitle, wcDesc;
            switch (wcWeek % 4)
            {
                case 0:
                    wcEmoji = "📝"; wcTitle = "Note Sprint"; wcDesc = "Add 5 progress notes across any goals";
                    wcTarget = 5; wcCurrent = WeekProgressNotes; break;
                case 1:
                    wcEmoji = "📓"; wcTitle = "Journal Week"; wcDesc = "Write 3 journal entries this week";
                    wcTarget = 3; wcCurrent = WeekJournalEntries; break;
                case 2:
                    wcEmoji = "✅"; wcTitle = "Todo Blitz"; wcDesc = "Complete 5 todos this week";
                    wcTarget = 5; wcCurrent = WeekTodosCompleted; break;
                default:
                    wcEmoji = "🎯"; wcTitle = "Goal Explorer"; wcDesc = "Work on 3 different goals this week";
                    wcTarget = 3; wcCurrent = recentProgress.Where(p => p.DeletedAt == null).Select(p => p.GoalFk).Distinct().Count(); break;
            }
            var wcDone = wcCurrent >= wcTarget;
            WeeklyChallengeTitle = $"{wcEmoji} {wcTitle}";
            WeeklyChallengeDesc = wcDesc;
            WeeklyChallengeDone = wcDone;
            WeeklyChallengePctValue = Math.Min(wcCurrent / (double)wcTarget, 1.0);
            WeeklyChallengeStatus = wcDone ? "✓ Done! 🎉" : $"{wcCurrent}/{wcTarget}";
            var wcLeft = wcTarget - wcCurrent;
            WeeklyChallengeMotivation = wcDone
                ? "🌟 Challenge complete — you crushed it this week!"
                : wcCurrent > 0 ? $"{wcLeft} more to go — you can do this! 💪"
                : "Start now and build momentum!";
            HasWeeklyChallenge = true;
        }
        else
        {
            HasWeeklyChallenge = false;
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
        analytics.Track("journal_quick_save");
        var weekStartMs = DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeMilliseconds();
        WeekJournalEntries = await journalRepo.GetCountSinceAsync(_accountGuid, weekStartMs);
        JournalThisWeek = WeekJournalEntries;
        HasWeeklyWins = WeekTodosCompleted > 0 || WeekProgressNotes > 0 || WeekJournalEntries > 0;
        await Task.Delay(1500);
        QuickJournalSaved = false;
        var journals = await journalRepo.GetRecentAsync(_accountGuid, 3);
        RecentJournals = new ObservableCollection<Journal>(journals);
    }

    [RelayCommand]
    private async Task GoToStaleGoalAsync()
    {
        if (!string.IsNullOrEmpty(StaleGoalGuid))
            await _nav.GoToAsync($"goals/entry?guid={StaleGoalGuid}");
    }

    [RelayCommand]
    private async Task QuickNoteForFocusGoalAsync()
    {
        if (string.IsNullOrEmpty(StaleGoalGuid)) return;
        var goalName = StaleGoalText.Length > 60 ? StaleGoalText[..60] + "…" : StaleGoalText;
        var note = await _nav.DisplayPromptAsync(
            "📝 Quick Note",
            $"Progress note for:\n\"{goalName}\"",
            "Save", "Cancel",
            "What progress did you make?",
            500);
        if (string.IsNullOrWhiteSpace(note)) return;
        if (string.IsNullOrEmpty(_accountGuid)) return;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await progressRepo.SaveAsync(new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = _accountGuid,
            GoalFk = StaleGoalGuid,
            NextStepItems = note.Trim(),
            UpdatedOn = ts
        });
        analytics.Track("dashboard_quick_note_focus");
        StaleGoalText = string.Empty;
        StaleGoalGuid = string.Empty;
        HasStaleGoal = false;
    }

    [RelayCommand]
    private async Task AddJournalAsync() =>
        await _nav.GoToAsync("journal/entry");

    [RelayCommand]
    private async Task OpenJournalAsync(Journal journal)
    {
        if (journal is null) return;
        await _nav.GoToAsync($"journal/entry?guid={journal.Guid}");
    }

    [RelayCommand]
    private async Task GoToSettingsAsync() =>
        await _nav.GoToAsync("settings");

    [RelayCommand]
    private async Task GoToGoalsAsync() =>
        await _nav.GoToAsync("//goals");

    [RelayCommand]
    private async Task GoToTodosAsync() =>
        await _nav.GoToAsync("//todos");

    [RelayCommand]
    private async Task GoToJournalAsync() =>
        await _nav.GoToAsync("//journal");

    [RelayCommand]
    private Task OpenRemindersAsync() => _nav.GoToAsync("reminders");
}
