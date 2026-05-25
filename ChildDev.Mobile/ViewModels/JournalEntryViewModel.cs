using LevelUp.Data;
using LevelUp.Models;
using LevelUp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LevelUp.ViewModels;

[QueryProperty(nameof(Guid), "guid")]
public partial class JournalEntryViewModel(
    JournalRepository repo,
    AccountService accountService,
    MobileAnalyticsService analytics) : ObservableObject
{
    [ObservableProperty] private string guid = string.Empty;
    [ObservableProperty] private string notes = string.Empty;
    [ObservableProperty] private string activity = string.Empty;
    [ObservableProperty] private string mood = string.Empty;
    [ObservableProperty] private string emotionReason = string.Empty;
    [ObservableProperty] private string tags = string.Empty;
    [ObservableProperty] private string enteredDateDisplay = string.Empty;
    [ObservableProperty] private DateTime enteredDate = DateTime.Today;
    [ObservableProperty] private bool isExisting;
    [ObservableProperty] private int notesWordCount;
    [ObservableProperty] private int activityLength;
    [ObservableProperty] private int moodLength;
    [ObservableProperty] private int emotionReasonLength;
    [ObservableProperty] private int tagsLength;

    partial void OnNotesChanged(string value)
    {
        NotesWordCount = string.IsNullOrWhiteSpace(value)
            ? 0
            : value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnActivityChanged(string value)
    {
        ActivityLength = value?.Length ?? 0;
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnMoodChanged(string value) => MoodLength = value?.Length ?? 0;
    partial void OnEmotionReasonChanged(string value) => EmotionReasonLength = value?.Length ?? 0;
    partial void OnTagsChanged(string value) => TagsLength = value?.Length ?? 0;

    private bool CanSave() => !string.IsNullOrWhiteSpace(Notes) || !string.IsNullOrWhiteSpace(Activity);

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
        EmotionReason = item.EmotionReason ?? string.Empty;
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

        var enteredMs = new DateTimeOffset(DateTime.SpecifyKind(EnteredDate, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        var journal = string.IsNullOrEmpty(Guid)
            ? new Journal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, EnteredDate = enteredMs }
            : await repo.GetAsync(Guid) ?? new Journal { Guid = Guid, AccountFk = account.Guid, EnteredDate = enteredMs };

        journal.EnteredDate = enteredMs;
        journal.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        journal.Activity = string.IsNullOrWhiteSpace(Activity) ? null : Activity.Trim();
        journal.Mood = string.IsNullOrWhiteSpace(Mood) ? null : Mood.Trim();
        journal.EmotionReason = string.IsNullOrWhiteSpace(EmotionReason) ? null : EmotionReason.Trim();
        journal.Tags = string.IsNullOrWhiteSpace(Tags) ? null : Tags.Trim();

        await repo.SaveAsync(journal);
        analytics.Track(string.IsNullOrEmpty(Guid) ? "journal_create" : "journal_save");
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (string.IsNullOrEmpty(Guid)) return;
        var confirmed = await Shell.Current.DisplayAlert("Delete Entry?", "Remove this journal entry?", "Delete", "Cancel");
        if (!confirmed) return;
        await repo.DeleteAsync(Guid);
        await Shell.Current.GoToAsync("..");
    }
}
