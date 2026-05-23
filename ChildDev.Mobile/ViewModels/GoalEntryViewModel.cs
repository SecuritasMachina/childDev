using LevelUp.Data;
using LevelUp.Models;
using LevelUp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LevelUp.ViewModels;

[QueryProperty(nameof(Guid), "guid")]
public partial class GoalEntryViewModel(
    GoalRepository repo,
    GoalProgressRepository progressRepo,
    AccountService accountService) : ObservableObject
{
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

    [ObservableProperty] private int goalTextLength;
    [ObservableProperty] private int measurableOutcomeLength;
    [ObservableProperty] private int nextStepItemsLength;

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
        var progress = await progressRepo.GetForGoalAsync(guid);
        NextStepItems = (progress.FirstOrDefault()?.NextStepItems ?? string.Empty).Trim();
        _loadedNextStepItems = NextStepItems;
        EnteredDateDisplay = DateTimeOffset.FromUnixTimeMilliseconds(item.EnteredDate).LocalDateTime.ToString("ddd, MMM d yyyy");
        IsExisting = true;
        IsCompleted = item.CompletionDate.HasValue;
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
        goal.Category = string.IsNullOrWhiteSpace(Category) ? null : Category;
        goal.ProgressPercent = ProgressPercent > 0 ? ProgressPercent : null;
        goal.IsPinned = IsPinned;
        goal.MeasurableOutcome = string.IsNullOrWhiteSpace(MeasurableOutcome) ? null : MeasurableOutcome.Trim();
        goal.NextMeetingDate = HasNextMeetingDate
            ? new DateTimeOffset(DateTime.SpecifyKind(NextMeetingDate, DateTimeKind.Local)).ToUnixTimeMilliseconds()
            : null;
        goal.ExpirationDate = HasExpirationDate
            ? new DateTimeOffset(DateTime.SpecifyKind(ExpirationDate, DateTimeKind.Local)).ToUnixTimeMilliseconds()
            : null;
        await repo.SaveAsync(goal);

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

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task MarkCompleteAsync()
    {
        if (string.IsNullOrEmpty(Guid)) return;
        await repo.CompleteAsync(Guid);
        await Shell.Current.GoToAsync("..");
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
        var confirmed = await Shell.Current.DisplayAlert("Delete Goal?", "Remove this goal and all its progress notes?", "Delete", "Cancel");
        if (!confirmed) return;
        await repo.DeleteAsync(Guid);
        await progressRepo.DeleteForGoalAsync(Guid);
        await Shell.Current.GoToAsync("..");
    }
}
