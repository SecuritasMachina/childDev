using System.Collections.ObjectModel;
using LevelUp.Models;
using LevelUp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LevelUp.ViewModels;

public partial class RemindersViewModel(
    ReminderService reminderService,
    AccountService accountService,
    INavigationService nav) : ObservableObject
{
    [ObservableProperty] private ObservableCollection<Reminder> reminders = [];
    [ObservableProperty] private bool hasReminders;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string newReminderTitle = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;
        IsLoading = true;
        try
        {
            var pending = await reminderService.GetPendingAsync(account.Guid);
            Reminders = new ObservableCollection<Reminder>(pending);
            HasReminders = Reminders.Count > 0;
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task SnoozeAsync(Reminder reminder)
    {
        var duration = await SnoozeHelper.PickAsync(nav);
        if (duration is null) return;
        await reminderService.SnoozeAsync(reminder, duration.Value);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DismissAsync(Reminder reminder)
    {
        await reminderService.DismissAsync(reminder);
        Reminders.Remove(reminder);
        HasReminders = Reminders.Count > 0;
    }

    [RelayCommand(CanExecute = nameof(CanAddGeneral))]
    private async Task AddGeneralAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;
        if (string.IsNullOrWhiteSpace(NewReminderTitle)) return;

        var duration = await SnoozeHelper.PickAsync(nav);
        if (duration is null) return;

        var reminder = new Reminder
        {
            AccountFk = account.Guid,
            Title = NewReminderTitle.Trim(),
            Topic = "General",
            FireAt = DateTimeOffset.UtcNow.Add(duration.Value).ToUnixTimeMilliseconds()
        };
        await reminderService.ScheduleAsync(reminder);
        NewReminderTitle = string.Empty;
        await LoadAsync();
    }

    private bool CanAddGeneral() => !string.IsNullOrWhiteSpace(NewReminderTitle);

    partial void OnNewReminderTitleChanged(string value) =>
        AddGeneralCommand.NotifyCanExecuteChanged();
}
