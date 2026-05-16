using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildDev.Mobile.ViewModels;

[QueryProperty(nameof(Guid), "guid")]
public partial class JournalEntryViewModel(
    JournalRepository repo,
    AccountService accountService) : ObservableObject
{
    [ObservableProperty] private string guid = string.Empty;
    [ObservableProperty] private string notes = string.Empty;
    [ObservableProperty] private string activity = string.Empty;
    [ObservableProperty] private string mood = string.Empty;
    [ObservableProperty] private string tags = string.Empty;
    [ObservableProperty] private string enteredDateDisplay = string.Empty;

    partial void OnGuidChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
            LoadAsync(value).FireAndForget();
    }

    private async Task LoadAsync(string guid)
    {
        var item = await repo.GetAsync(guid);
        if (item is null) return;
        Notes = item.Notes ?? string.Empty;
        Activity = item.Activity ?? string.Empty;
        Mood = item.Mood ?? string.Empty;
        Tags = item.Tags ?? string.Empty;
        EnteredDateDisplay = DateTimeOffset.FromUnixTimeMilliseconds(item.EnteredDate).LocalDateTime.ToString("ddd, MMM d yyyy");
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;

        var journal = string.IsNullOrEmpty(Guid)
            ? new Journal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
            : await repo.GetAsync(Guid) ?? new Journal { Guid = Guid, AccountFk = account.Guid, EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };

        journal.Notes = Notes;
        journal.Activity = Activity;
        journal.Mood = Mood;
        journal.Tags = Tags;

        await repo.SaveAsync(journal);
        await Shell.Current.GoToAsync("..");
    }
}
