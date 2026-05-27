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
public partial class GoalEntryViewModel(
    GoalRepository repo,
    GoalProgressRepository progressRepo,
    TodoRepository todoRepo,
    AccountService accountService,
    MobileAnalyticsService analytics,
    INavigationService nav,
    ReminderService reminderService) : ObservableObject
{
    private readonly INavigationService _nav = nav;
    private readonly ReminderService _reminderService = reminderService;
    [ObservableProperty] private string guid = string.Empty;
    [ObservableProperty] private string goalText = string.Empty;
    [ObservableProperty] private string measurableOutcome = string.Empty;
    [ObservableProperty] private string nextStepItems = string.Empty;
    [ObservableProperty] private DateTime nextMeetingDate = DateTime.Today.AddDays(7);
    [ObservableProperty] private bool hasNextMeetingDate;
    [ObservableProperty] private DateTime expirationDate = DateTime.Today.AddMonths(3);
    [ObservableProperty] private bool hasExpirationDate;
    [ObservableProperty] private bool isExisting;
    [ObservableProperty] private bool isCompleted;
    [ObservableProperty] private string enteredDateDisplay = string.Empty;
    [ObservableProperty] private string? category;
    [ObservableProperty] private int progressPercent;
    [ObservableProperty] private bool isPinned;
    [ObservableProperty] private string steps = string.Empty;

    [ObservableProperty] private int goalTextLength;
    [ObservableProperty] private int measurableOutcomeLength;
    [ObservableProperty] private int nextStepItemsLength;
    [ObservableProperty] private string tierLabel = string.Empty;
    [ObservableProperty] private string nextTierLabel = string.Empty;
    [ObservableProperty] private int progressNotesCount;
    [ObservableProperty] private ObservableCollection<GoalProgress> progressHistory = [];
    [ObservableProperty] private bool hasProgressHistory;
    [ObservableProperty] private ObservableCollection<Todo> linkedTodos = [];
    [ObservableProperty] private bool hasLinkedTodos;

    public double ProgressBarValue => ProgressPercent / 100.0;

    partial void OnProgressPercentChanged(int value) => OnPropertyChanged(nameof(ProgressBarValue));

    private string _loadedNextStepItems = string.Empty;

    private bool CanSave() => !string.IsNullOrWhiteSpace(GoalText);

    partial void OnGoalTextChanged(string value)
    {
        GoalTextLength = value?.Length ?? 0;
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnMeasurableOutcomeChanged(string value) =>
        MeasurableOutcomeLength = value?.Length ?? 0;

    partial void OnNextStepItemsChanged(string value) =>
        NextStepItemsLength = value?.Length ?? 0;

    partial void OnGuidChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
            LoadAsync(value).FireAndForget();
    }

    private async Task LoadAsync(string guid)
    {
        var item = await repo.GetAsync(guid);
        if (item is null) return;
        GoalText = item.GoalText ?? string.Empty;
        MeasurableOutcome = item.MeasurableOutcome ?? string.Empty;
        if (item.NextMeetingDate.HasValue)
        {
            NextMeetingDate = DateTimeOffset.FromUnixTimeMilliseconds(item.NextMeetingDate.Value).LocalDateTime;
            HasNextMeetingDate = true;
        }
        if (item.ExpirationDate.HasValue)
        {
            ExpirationDate = DateTimeOffset.FromUnixTimeMilliseconds(item.ExpirationDate.Value).LocalDateTime;
            HasExpirationDate = true;
        }
        Category = item.Category;
        ProgressPercent = item.ProgressPercent ?? 0;
        IsPinned = item.IsPinned;
        Steps = item.Steps ?? string.Empty;
        var progress = await progressRepo.GetForGoalAsync(guid);
        NextStepItems = (progress.FirstOrDefault()?.NextStepItems ?? string.Empty).Trim();
        _loadedNextStepItems = NextStepItems;
        ProgressNotesCount = progress.Count;
        var history = progress.Skip(1).Take(4).Where(p => !string.IsNullOrWhiteSpace(p.NextStepItems)).ToList();
        ProgressHistory = new ObservableCollection<GoalProgress>(history);
        HasProgressHistory = history.Count > 0;
        TierLabel = ProgressNotesCount switch
        {
            >= 200 => "🌟 Legend",
            >= 100 => "🏆 Master",
            >= 60  => "💎 Expert",
            >= 30  => "⭐ Skilled",
            >= 15  => "🚀 Apprentice",
            >= 5   => "🌱 Beginner",
            _      => string.Empty
        };
        NextTierLabel = ProgressNotesCount switch
        {
            >= 200 => string.Empty,
            >= 100 => $"{200 - ProgressNotesCount} more notes to 🌟 Legend",
            >= 60  => $"{100 - ProgressNotesCount} more notes to 🏆 Master",
            >= 30  => $"{60 - ProgressNotesCount} more notes to 💎 Expert",
            >= 15  => $"{30 - ProgressNotesCount} more notes to ⭐ Skilled",
            >= 5   => $"{15 - ProgressNotesCount} more notes to 🚀 Apprentice",
            _      => $"{5 - ProgressNotesCount} more notes to 🌱 Beginner"
        };
        EnteredDateDisplay = DateTimeOffset.FromUnixTimeMilliseconds(item.EnteredDate).LocalDateTime.ToString("ddd, MMM d yyyy");
        IsExisting = true;
        IsCompleted = item.CompletionDate.HasValue;
        analytics.Track("goal_view", item.Category);

        if (!string.IsNullOrWhiteSpace(item.GoalText))
        {
            var account = await accountService.GetAccountAsync();
            if (account is not null)
            {
                var pending = await todoRepo.GetPendingAsync(account.Guid);
                var trimmedGoalText = item.GoalText.Trim();
                if (trimmedGoalText.Length > 1994) trimmedGoalText = trimmedGoalText[..1994];
                var goalPrefix = $"Goal: {trimmedGoalText}";
                var linked = pending
                    .Where(t => t.Notes != null &&
                        t.Notes.StartsWith(goalPrefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                LinkedTodos = new ObservableCollection<Todo>(linked);
                HasLinkedTodos = linked.Count > 0;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = string.IsNullOrEmpty(Guid)
            ? new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, EnteredDate = ts }
            : await repo.GetAsync(Guid) ?? new Goal { Guid = Guid, AccountFk = account.Guid, EnteredDate = ts };

        goal.GoalText = GoalText.Trim();
        var cat = string.IsNullOrWhiteSpace(Category) ? null : Category.Trim();
        goal.Category = cat is { Length: > 50 } ? cat[..50] : cat;
        goal.ProgressPercent = ProgressPercent > 0 ? ProgressPercent : null;
        goal.IsPinned = IsPinned;
        goal.Steps = string.IsNullOrWhiteSpace(Steps) ? null : Steps.Trim();
        goal.MeasurableOutcome = string.IsNullOrWhiteSpace(MeasurableOutcome) ? null : MeasurableOutcome.Trim();
        goal.NextMeetingDate = HasNextMeetingDate
            ? new DateTimeOffset(DateTime.SpecifyKind(NextMeetingDate, DateTimeKind.Local)).ToUnixTimeMilliseconds()
            : null;
        goal.ExpirationDate = HasExpirationDate
            ? new DateTimeOffset(DateTime.SpecifyKind(ExpirationDate, DateTimeKind.Local)).ToUnixTimeMilliseconds()
            : null;
        await repo.SaveAsync(goal);
        analytics.Track(string.IsNullOrEmpty(Guid) ? "goal_create" : "goal_save", goal.Category);

        var trimmedNextStep = NextStepItems.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedNextStep) && trimmedNextStep != _loadedNextStepItems)
        {
            var progress = new GoalProgress
            {
                Guid = System.Guid.NewGuid().ToString(),
                AccountFk = account.Guid,
                GoalFk = goal.Guid,
                NextStepItems = trimmedNextStep,
                NextMeetingDate = goal.NextMeetingDate,
                UpdatedOn = ts
            };
            await progressRepo.SaveAsync(progress);
        }

        await _nav.GoToAsync("..");
    }

    [RelayCommand]
    private void SetNoteTemplate(string prefix)
    {
        if (!NextStepItems.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            NextStepItems = prefix;
    }

    [RelayCommand]
    private async Task AddLinkedTodoAsync()
    {
        if (string.IsNullOrEmpty(Guid)) return;
        var goalName = GoalText.Length > 60 ? GoalText[..60] + "…" : GoalText;
        var title = await _nav.DisplayPromptAsync(
            "➕ Add Todo",
            $"For goal: \"{goalName}\"",
            "Add", "Cancel",
            "What needs to be done?",
            200);
        if (string.IsNullOrWhiteSpace(title)) return;
        var account = await accountService.GetAccountAsync();
        if (account is null) return;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            Title = title.Trim(),
            Notes = $"Goal: {(GoalText.Trim() is { Length: > 1994 } gt ? gt[..1994] : GoalText.Trim())}",
            UpdatedOn = ts
        };
        await todoRepo.SaveAsync(todo);
        analytics.Track("goal_add_todo", null);
        LinkedTodos.Insert(0, todo);
        HasLinkedTodos = true;
    }

    [RelayCommand]
    private async Task CompleteLinkedTodoAsync(Todo todo)
    {
        if (todo is null) return;
        await todoRepo.CompleteAsync(todo.Guid);
        analytics.Track("goal_todo_complete_inline", null);
        LinkedTodos.Remove(todo);
        HasLinkedTodos = LinkedTodos.Count > 0;
    }

    [RelayCommand]
    private async Task ShareProgressAsync()
    {
        if (string.IsNullOrEmpty(Guid)) return;
        var allNotes = await progressRepo.GetForGoalAsync(Guid);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Goal: {GoalText}");
        if (!string.IsNullOrWhiteSpace(MeasurableOutcome))
            sb.AppendLine($"Success measure: {MeasurableOutcome}");
        if (IsCompleted)
            sb.AppendLine("Status: Completed ✓");
        sb.AppendLine($"Progress notes: {allNotes.Count}");
        sb.AppendLine();
        var ordered = allNotes.OrderBy(p => p.UpdatedOn).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var p = ordered[i];
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(p.UpdatedOn).LocalDateTime;
            sb.AppendLine($"[{dt:MMM d, yyyy}] Note #{i + 1}");
            if (!string.IsNullOrWhiteSpace(p.NextStepItems))
                sb.AppendLine(p.NextStepItems);
            sb.AppendLine();
        }
        var summaryText = sb.ToString().TrimEnd();
        analytics.Track("goal_share_progress");
#if !NO_MAUI
        await Share.RequestAsync(new ShareTextRequest
        {
            Title = $"Progress: {(GoalText.Length > 60 ? GoalText[..60] + "…" : GoalText)}",
            Text = summaryText
        });
#else
        await Task.CompletedTask;
#endif
    }

    [RelayCommand]
    private async Task MarkCompleteAsync()
    {
        if (string.IsNullOrEmpty(Guid)) return;
        await repo.CompleteAsync(Guid);
        analytics.Track("goal_complete");
        var goalName = GoalText.Length > 60 ? GoalText[..60] + "…" : GoalText;
        await _nav.AlertAsync("🎉 Goal Complete!", $"Amazing work! You've achieved \"{goalName}\" — take a moment to celebrate this win! 🌟", "Celebrate! 🎊");
        await _nav.GoToAsync("..");
    }

    [RelayCommand]
    private async Task ReopenAsync()
    {
        if (string.IsNullOrEmpty(Guid)) return;
        await repo.ReopenAsync(Guid);
        IsCompleted = false;
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (string.IsNullOrEmpty(Guid)) return;
        var confirmed = await _nav.DisplayAlertAsync("Delete Goal?", "Remove this goal and all its progress notes?", "Delete", "Cancel");
        if (!confirmed) return;
        await repo.DeleteAsync(Guid);
        await progressRepo.DeleteForGoalAsync(Guid);
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

        var reminder = new LevelUp.Models.Reminder
        {
            AccountFk = account.Guid,
            Topic = "Goal",
            EntityGuid = Guid,
            Title = $"Goal: {(GoalText?.Length > 40 ? GoalText[..40] + "…" : GoalText)}",
            EntityLabel = GoalText,
            FireAt = DateTimeOffset.UtcNow.Add(duration.Value).ToUnixTimeMilliseconds()
        };
        await _reminderService.ScheduleAsync(reminder);
        await _nav.AlertAsync("Reminder Set", $"You'll be reminded in {FormatDuration(duration.Value)}.", "OK");
    }

    private static string FormatDuration(TimeSpan d) => d.TotalDays >= 1
        ? $"{(int)d.TotalDays} day{((int)d.TotalDays == 1 ? "" : "s")}"
        : $"{(int)d.TotalHours} hour{((int)d.TotalHours == 1 ? "" : "s")}";
}
