using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildDev.Mobile.ViewModels;

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
    [ObservableProperty] private DateTime expirationDate = DateTime.Today.AddMonths(3);
    [ObservableProperty] private bool hasExpirationDate;
    [ObservableProperty] private bool isExisting;
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
        GoalText = item.GoalText ?? string.Empty;
        MeasurableOutcome = item.MeasurableOutcome ?? string.Empty;
        if (item.NextMeetingDate.HasValue)
            NextMeetingDate = DateTimeOffset.FromUnixTimeMilliseconds(item.NextMeetingDate.Value).LocalDateTime;
        if (item.ExpirationDate.HasValue)
        {
            ExpirationDate = DateTimeOffset.FromUnixTimeMilliseconds(item.ExpirationDate.Value).LocalDateTime;
            HasExpirationDate = true;
        }
        var progress = await progressRepo.GetForGoalAsync(guid);
        NextStepItems = progress.FirstOrDefault()?.NextStepItems ?? string.Empty;
        EnteredDateDisplay = DateTimeOffset.FromUnixTimeMilliseconds(item.EnteredDate).LocalDateTime.ToString("ddd, MMM d yyyy");
        IsExisting = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = string.IsNullOrEmpty(Guid)
            ? new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, EnteredDate = ts }
            : await repo.GetAsync(Guid) ?? new Goal { Guid = Guid, AccountFk = account.Guid, EnteredDate = ts };

        goal.GoalText = GoalText;
        goal.MeasurableOutcome = MeasurableOutcome;
        goal.NextMeetingDate = new DateTimeOffset(NextMeetingDate, TimeSpan.Zero).ToUnixTimeMilliseconds();
        goal.ExpirationDate = HasExpirationDate
            ? new DateTimeOffset(ExpirationDate, TimeSpan.Zero).ToUnixTimeMilliseconds()
            : null;
        await repo.SaveAsync(goal);

        if (!string.IsNullOrWhiteSpace(NextStepItems))
        {
            var progress = new GoalProgress
            {
                Guid = System.Guid.NewGuid().ToString(),
                AccountFk = account.Guid,
                GoalFk = goal.Guid,
                NextStepItems = NextStepItems,
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
}
