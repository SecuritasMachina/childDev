using System.Collections.ObjectModel;
using LevelUp.Data;
using LevelUp.Models;
using LevelUp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LevelUp.ViewModels;

#if !NO_MAUI
[QueryProperty(nameof(Guid), "guid")]
#endif
public partial class TodoEntryViewModel(
    TodoRepository repo,
    GoalRepository goalRepo,
    AccountService accountService,
    MobileAnalyticsService analytics,
    INavigationService nav,
    ReminderService reminderService) : ObservableObject
{
    [ObservableProperty] private string guid = string.Empty;
    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private int titleLength;
    [ObservableProperty] private string notes = string.Empty;
    [ObservableProperty] private int notesLength;
    [ObservableProperty] private bool hasDueDate;
    [ObservableProperty] private DateTime dueDate = DateTime.Today.AddDays(1);
    [ObservableProperty] private bool isExisting;
    [ObservableProperty] private bool isCompleted;
    [ObservableProperty] private ObservableCollection<Goal> availableGoals = [];
    [ObservableProperty] private Goal? linkedGoal;

    private readonly INavigationService _nav = nav;
    private readonly ReminderService _reminderService = reminderService;

    private bool CanSave() => !string.IsNullOrWhiteSpace(Title);

    partial void OnLinkedGoalChanged(Goal? value)
    {
        if (value is null) return;
        var gt = value.GoalText ?? string.Empty;
        if (gt.Length > 1994) gt = gt[..1994];
        var goalPrefix = $"Goal: {gt}";
        var existingNotes = Notes ?? string.Empty;
        if (existingNotes.StartsWith("Goal: ", StringComparison.OrdinalIgnoreCase))
        {
            var afterGoalLine = existingNotes.Contains('\n') ? existingNotes[(existingNotes.IndexOf('\n') + 1)..] : string.Empty;
            var combined = string.IsNullOrWhiteSpace(afterGoalLine) ? goalPrefix : $"{goalPrefix}\n{afterGoalLine}";
            Notes = combined.Length > 2000 ? combined[..2000] : combined;
        }
        else
        {
            var combined = string.IsNullOrWhiteSpace(existingNotes) ? goalPrefix : $"{goalPrefix}\n{existingNotes}";
            Notes = combined.Length > 2000 ? combined[..2000] : combined;
        }
    }

    [RelayCommand]
    private void SetDueToday() { DueDate = DateTime.Today; HasDueDate = true; }

    [RelayCommand]
    private void SetDueTomorrow() { DueDate = DateTime.Today.AddDays(1); HasDueDate = true; }

    [RelayCommand]
    private void SetDueThisWeek()
    {
        var daysUntilFriday = ((int)DayOfWeek.Friday - (int)DateTime.Today.DayOfWeek + 7) % 7;
        DueDate = DateTime.Today.AddDays(daysUntilFriday == 0 ? 7 : daysUntilFriday);
        HasDueDate = true;
    }

    partial void OnTitleChanged(string value)
    {
        TitleLength = value?.Length ?? 0;
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnNotesChanged(string value) => NotesLength = value?.Length ?? 0;

    partial void OnGuidChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
            LoadAsync(value).FireAndForget();
        else
            LoadGoalsAsync().FireAndForget();
    }

    private async Task LoadGoalsAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;
        var goals = await goalRepo.GetAllActiveAsync(account.Guid);
        var active = goals.Where(g => g.CompletionDate is null).ToList();
        AvailableGoals = new ObservableCollection<Goal>(active);
    }

    private async Task LoadAsync(string guid)
    {
        var account = await accountService.GetAccountAsync();
        if (account is not null)
        {
            var goals = await goalRepo.GetAllActiveAsync(account.Guid);
            var active = goals.Where(g => g.CompletionDate is null).ToList();
            AvailableGoals = new ObservableCollection<Goal>(active);
        }
        var item = await repo.GetAsync(guid);
        if (item is null) return;
        Title = item.Title ?? string.Empty;
        Notes = item.Notes ?? string.Empty;
        if (item.DueDate.HasValue)
        {
            DueDate = DateTimeOffset.FromUnixTimeMilliseconds(item.DueDate.Value).LocalDateTime;
            HasDueDate = true;
        }
        IsExisting = true;
        IsCompleted = item.CompletedAt.HasValue;
        // Detect linked goal from Notes prefix
        if (!string.IsNullOrWhiteSpace(Notes) && Notes.StartsWith("Goal: ", StringComparison.OrdinalIgnoreCase))
        {
            var goalLine = Notes.Contains('\n') ? Notes[..Notes.IndexOf('\n')] : Notes;
            var goalText = goalLine["Goal: ".Length..].Trim();
            LinkedGoal = AvailableGoals.FirstOrDefault(g =>
                string.Equals(g.GoalText?.Trim(), goalText, StringComparison.OrdinalIgnoreCase));
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = string.IsNullOrEmpty(Guid)
            ? new Todo { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid }
            : await repo.GetAsync(Guid) ?? new Todo { Guid = Guid, AccountFk = account.Guid };

        todo.Title = Title.Trim();
        todo.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        todo.DueDate = HasDueDate
            ? new DateTimeOffset(DateTime.SpecifyKind(DueDate, DateTimeKind.Local)).ToUnixTimeMilliseconds()
            : null;

        await repo.SaveAsync(todo);
        analytics.Track(string.IsNullOrEmpty(Guid) ? "todo_create" : "todo_edit");
        await _nav.GoToAsync("..");
    }

    [RelayCommand]
    private async Task MarkDoneAsync()
    {
        if (string.IsNullOrEmpty(Guid)) return;
        await repo.CompleteAsync(Guid);
        analytics.Track("todo_complete");
        await _nav.GoToAsync("..");
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (string.IsNullOrEmpty(Guid)) return;
        await repo.UncompleteAsync(Guid);
        await _nav.GoToAsync("..");
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (string.IsNullOrEmpty(Guid)) return;
        var confirmed = await _nav.DisplayAlertAsync("Delete Todo?", "Remove this todo?", "Delete", "Cancel");
        if (!confirmed) return;
        await repo.DeleteAsync(Guid);
        await _nav.GoToAsync("..");
    }

    [RelayCommand]
    private async Task SetReminderAsync()
    {
        if (string.IsNullOrEmpty(Guid)) return;
        var account = await accountService.GetAccountAsync();
        if (account is null) return;

        var duration = await SnoozeHelper.PickAsync(_nav);
        if (duration is null) return;

        var todoTitle = Title;
        var reminder = new LevelUp.Models.Reminder
        {
            AccountFk = account.Guid,
            Topic = "Todo",
            EntityGuid = Guid,
            Title = $"Todo: {(todoTitle?.Length > 40 ? todoTitle[..40] + "…" : todoTitle)}",
            EntityLabel = todoTitle,
            FireAt = DateTimeOffset.UtcNow.Add(duration.Value).ToUnixTimeMilliseconds()
        };
        await _reminderService.ScheduleAsync(reminder);
        await _nav.AlertAsync("Reminder Set", $"You'll be reminded in {FormatDuration(duration.Value)}.", "OK");
    }

    private static string FormatDuration(TimeSpan d) => d.TotalDays >= 1
        ? $"{(int)d.TotalDays} day{((int)d.TotalDays == 1 ? "" : "s")}"
        : $"{(int)d.TotalHours} hour{((int)d.TotalHours == 1 ? "" : "s")}";
}
