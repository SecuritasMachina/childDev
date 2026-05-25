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

    private List<Journal> _allJournals = [];

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
