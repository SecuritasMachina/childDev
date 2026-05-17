using System.Collections.ObjectModel;
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildDev.Mobile.ViewModels;

public partial class JournalListViewModel(
    JournalRepository repo,
    AccountService accountService,
    SyncService syncService) : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Journal> journals = [];

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isRefreshing;

    [ObservableProperty]
    private string filterText = string.Empty;

    private List<Journal> _allJournals = [];

    partial void OnFilterTextChanged(string value) =>
        Journals = new ObservableCollection<Journal>(
            string.IsNullOrWhiteSpace(value)
                ? _allJournals
                : _allJournals.Where(j =>
                    (j.Notes != null && j.Notes.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
                    (j.Activity != null && j.Activity.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
                    (j.Mood != null && j.Mood.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
                    (j.Tags != null && j.Tags.Contains(value, StringComparison.OrdinalIgnoreCase))));

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            StatusMessage = string.Empty;
            var account = await accountService.GetAccountAsync();
            if (account is null) return;
            var items = await repo.GetAllActiveAsync(account.Guid);
            _allJournals = items;
            Journals = new ObservableCollection<Journal>(items);
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

    [RelayCommand]
    private async Task AddAsync() =>
        await Shell.Current.GoToAsync("journal/entry");

    [RelayCommand]
    private async Task OpenAsync(Journal journal) =>
        await Shell.Current.GoToAsync($"journal/entry?guid={journal.Guid}");

    [RelayCommand]
    private async Task DeleteAsync(Journal journal)
    {
        await repo.DeleteAsync(journal.Guid);
        _allJournals.Remove(journal);
        Journals.Remove(journal);
    }
}
