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
    [ObservableProperty] private DateTime enteredDate = DateTime.Today;
    [ObservableProperty] private bool isExisting;
    [ObservableProperty] private int notesLength;

    partial void OnNotesChanged(string value)
    {
        NotesLength = value?.Length ?? 0;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private bool CanSave() => !string.IsNullOrWhiteSpace(Notes);

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
        EnteredDate = DateTimeOffset.FromUnixTimeMilliseconds(item.EnteredDate).LocalDateTime;
        EnteredDateDisplay = EnteredDate.ToString("ddd, MMM d yyyy");
        IsExisting = true;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;

        var enteredMs = new DateTimeOffset(EnteredDate, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var journal = string.IsNullOrEmpty(Guid)
            ? new Journal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, EnteredDate = enteredMs }
            : await repo.GetAsync(Guid) ?? new Journal { Guid = Guid, AccountFk = account.Guid, EnteredDate = enteredMs };

        journal.Notes = Notes.Trim();
        journal.Activity = string.IsNullOrWhiteSpace(Activity) ? null : Activity.Trim();
        journal.Mood = string.IsNullOrWhiteSpace(Mood) ? null : Mood.Trim();
        journal.Tags = string.IsNullOrWhiteSpace(Tags) ? null : Tags.Trim();

        await repo.SaveAsync(journal);
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (string.IsNullOrEmpty(Guid)) return;
        await repo.DeleteAsync(Guid);
        await Shell.Current.GoToAsync("..");
    }
}
