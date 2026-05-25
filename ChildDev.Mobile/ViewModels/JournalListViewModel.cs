using System.Collections.ObjectModel;
using LevelUp.Data;
using LevelUp.Models;
using LevelUp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LevelUp.ViewModels;

public partial class JournalListViewModel(
    JournalRepository repo,
    AccountService accountService,
    SyncService syncService,
    MobileAnalyticsService analytics) : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Journal> journals = [];

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isRefreshing;

    [ObservableProperty]
    private string filterText = string.Empty;

    [ObservableProperty]
    private string entryCountDisplay = string.Empty;

    [ObservableProperty]
    private string emptyMessage = "No journal entries yet";

    [ObservableProperty]
    private string streakDisplay = string.Empty;

    [ObservableProperty]
    private string todayPrompt = string.Empty;

    [ObservableProperty]
    private bool hasTodayEntry;

    [ObservableProperty]
    private string streakWarning = string.Empty;

    [ObservableProperty]
    private bool hasStreakWarning;

    private static readonly string[] Prompts =
    [
        "What's one thing you learned today?",
        "What made you smile today?",
        "What's something you want to get better at?",
        "What was the best part of your day?",
        "What's a challenge you faced and how did you handle it?",
        "What are you grateful for today?",
        "What's one thing you did today that you're proud of?",
        "What's something you're looking forward to?",
        "What would you do differently if you could redo today?",
        "Who helped you today and how?",
        "What goal did you make progress on today?",
        "What's one word that describes your mood today?",
    ];

    private List<Journal> _allJournals = [];
    private int _promptIdx;

    partial void OnFilterTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Journals = new ObservableCollection<Journal>(_allJournals);
            EmptyMessage = "No journal entries yet";
            UpdateEntryCountDisplay();
        }
        else
        {
            var filtered = _allJournals.Where(j =>
                (j.Notes != null && j.Notes.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
                (j.Activity != null && j.Activity.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
                (j.Mood != null && j.Mood.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
                (j.Tags != null && j.Tags.Contains(value, StringComparison.OrdinalIgnoreCase))).ToList();
            Journals = new ObservableCollection<Journal>(filtered);
            EmptyMessage = $"No matches for \"{value}\"";
            var n = filtered.Count;
            EntryCountDisplay = $"{n} {(n == 1 ? "entry" : "entries")} matching";
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
            analytics.Track("journal_list_view");
            var items = await repo.GetAllActiveAsync(account.Guid);
            _allJournals = items;
            Journals = new ObservableCollection<Journal>(items);
            UpdateEntryCountDisplay();
            var streak = await repo.GetJournalStreakAsync(account.Guid);
            StreakDisplay = streak >= 2
                ? $"{(streak >= 14 ? "🌟" : streak >= 7 ? "🔥" : "⭐")} {streak}-day journaling streak!"
                : string.Empty;
            HasTodayEntry = await repo.HasEntryTodayAsync(account.Guid);
            UpdateStreakWarning(streak, HasTodayEntry);
            _promptIdx = (int)(DateOnly.FromDateTime(DateTime.Today).DayNumber % Prompts.Length);
            TodayPrompt = Prompts[_promptIdx];
            UpdateStreakWarning(streak, HasTodayEntry);
        }
        catch
        {
            StatusMessage = "Could not load entries. Please restart the app.";
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
            var items = await repo.GetAllActiveAsync(account.Guid);
            _allJournals = items;
            Journals = new ObservableCollection<Journal>(items);
            UpdateEntryCountDisplay();
            var streak = await repo.GetJournalStreakAsync(account.Guid);
            StreakDisplay = streak >= 2
                ? $"{(streak >= 14 ? "🌟" : streak >= 7 ? "🔥" : "⭐")} {streak}-day journaling streak!"
                : string.Empty;
            HasTodayEntry = await repo.HasEntryTodayAsync(account.Guid);
            UpdateStreakWarning(streak, HasTodayEntry);
            StatusMessage = string.Empty;
        }
        catch
        {
            StatusMessage = "Could not refresh entries.";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void UpdateEntryCountDisplay()
    {
        var count = _allJournals.Count;
        EntryCountDisplay = $"{count} {(count == 1 ? "entry" : "entries")}";
    }

    private void UpdateStreakWarning(int streak, bool hasTodayEntry)
    {
        if (streak >= 3 && !hasTodayEntry)
        {
            StreakWarning = streak >= 7
                ? $"⚠️ Don't break your {streak}-day streak! Write a quick entry today."
                : $"🛡️ Protect your {streak}-day streak — add an entry today!";
            HasStreakWarning = true;
        }
        else
        {
            StreakWarning = string.Empty;
            HasStreakWarning = false;
        }
    }

    [RelayCommand]
    private void ShufflePrompt()
    {
        _promptIdx = (_promptIdx + 1) % Prompts.Length;
        TodayPrompt = Prompts[_promptIdx];
    }

    [RelayCommand]
    private async Task AddAsync() =>
        await Shell.Current.GoToAsync("journal/entry");

    [RelayCommand]
    private async Task OpenAsync(Journal journal) =>
        await Shell.Current.GoToAsync($"journal/entry?guid={journal.Guid}");

    [RelayCommand]
    private async Task DeleteAsync(Journal journal)
    {
        var confirmed = await Shell.Current.DisplayAlert("Delete Entry?", "Remove this journal entry?", "Delete", "Cancel");
        if (!confirmed) return;
        await repo.DeleteAsync(journal.Guid);
        _allJournals.Remove(journal);
        Journals.Remove(journal);
        UpdateEntryCountDisplay();
    }
}
