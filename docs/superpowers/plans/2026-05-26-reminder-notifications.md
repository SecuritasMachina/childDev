# Reminder Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add local push notifications to the LevelUp MAUI Android app so users can schedule reminders for goals, todos, journals, or general topics, with snooze options of 1h / 8h / 1d / 3d / custom (user-entered hours/days/weeks/months).

**Architecture:** `Plugin.LocalNotification` provides Android local notification scheduling. A thin `INotificationService` wraps it for testability. `ReminderService` orchestrates saving reminders to SQLite and scheduling/cancelling notifications. `RemindersViewModel` drives a new RemindersPage listing all pending reminders. GoalEntry, TodoEntry, and JournalEntry ViewModels get a `SetReminderCommand` that uses an action sheet for quick snooze durations.

**Tech Stack:** .NET MAUI 8 (Android), `Plugin.LocalNotification`, sqlite-net-pcl (in-memory for tests), CommunityToolkit.Mvvm, xUnit

---

## File Map

**New files:**
- `ChildDev.Mobile/Models/Reminder.cs` — reminder entity (device-local, no SyncBase)
- `ChildDev.Mobile/Data/ReminderRepository.cs` — CRUD for Reminder table
- `ChildDev.Mobile/Services/INotificationService.cs` — platform abstraction for scheduling local notifications
- `ChildDev.Mobile/Services/MauiNotificationService.cs` — production impl using Plugin.LocalNotification
- `ChildDev.Mobile/Services/ReminderService.cs` — orchestrates DB + notification scheduling
- `ChildDev.Mobile/Services/SnoozeHelper.cs` — shared helper to show snooze action sheet UI
- `ChildDev.Mobile/ViewModels/RemindersViewModel.cs` — list/snooze/dismiss pending reminders
- `ChildDev.Mobile/Views/RemindersPage.xaml` — reminder list UI
- `ChildDev.Mobile/Views/RemindersPage.xaml.cs` — code-behind
- `ChildDev.Mobile.Tests/ReminderServiceTests.cs` — unit tests
- `ChildDev.Mobile.Tests/RemindersViewModelTests.cs` — unit tests

**Modified files:**
- `ChildDev.Mobile/Services/INavigationService.cs` — add `DisplayActionSheetAsync`
- `ChildDev.Mobile/Services/MauiNavigationService.cs` — implement `DisplayActionSheetAsync`
- `ChildDev.Mobile/Data/LocalDatabase.cs` — add `CreateTableAsync<Reminder>()`
- `ChildDev.Mobile/MauiProgram.cs` — register new services/pages/VMs, call `.UseLocalNotification()`
- `ChildDev.Mobile/App.xaml.cs` — wire notification tap → navigation
- `ChildDev.Mobile/AppShell.xaml.cs` — register `reminders` route
- `ChildDev.Mobile/ViewModels/GoalEntryViewModel.cs` — add `SetReminderCommand`
- `ChildDev.Mobile/ViewModels/TodoEntryViewModel.cs` — add `SetReminderCommand`
- `ChildDev.Mobile/ViewModels/JournalEntryViewModel.cs` — add `SetReminderCommand`
- `ChildDev.Mobile/ViewModels/DashboardViewModel.cs` — add `OpenRemindersCommand`
- `ChildDev.Mobile.Tests/ViewModelTestBase.cs` — add `FakeNotificationService`, `ReminderRepository`, `ReminderService`; add `DisplayActionSheetAsync` to `FakeNavigationService`
- `ChildDev.Mobile/LevelUp.csproj` — add `Plugin.LocalNotification` package

---

## Task 1: Add Plugin.LocalNotification and extend INavigationService

**Files:**
- Modify: `ChildDev.Mobile/LevelUp.csproj`
- Modify: `ChildDev.Mobile/Services/INavigationService.cs`
- Modify: `ChildDev.Mobile/Services/MauiNavigationService.cs`
- Modify: `ChildDev.Mobile/MauiProgram.cs`

- [ ] **Step 1: Add Plugin.LocalNotification package**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
dotnet add ChildDev.Mobile/LevelUp.csproj package Plugin.LocalNotification
```

Expected output: Package added successfully. Verify it appears in `LevelUp.csproj` as a `PackageReference`. The package targets net8.0-android (and other MAUI platforms) automatically.

- [ ] **Step 2: Add DisplayActionSheetAsync to INavigationService**

Replace the entire contents of `ChildDev.Mobile/Services/INavigationService.cs`:

```csharp
namespace LevelUp.Services;

public interface INavigationService
{
    Task GoToAsync(string route);
    Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel);
    Task AlertAsync(string title, string message, string cancel);
    Task<string?> DisplayPromptAsync(string title, string message, string accept, string cancel, string placeholder, int maxLength);
    Task<string?> DisplayActionSheetAsync(string title, string cancel, string? destruction, params string[] buttons);
}
```

- [ ] **Step 3: Implement DisplayActionSheetAsync in MauiNavigationService**

Add this method to `ChildDev.Mobile/Services/MauiNavigationService.cs` (append before the closing brace):

```csharp
    public Task<string?> DisplayActionSheetAsync(string title, string cancel, string? destruction, params string[] buttons) =>
#if ANDROID || IOS || MACCATALYST || WINDOWS
        Shell.Current.DisplayActionSheet(title, cancel, destruction, buttons)!;
#else
        Task.FromResult<string?>(null);
#endif
```

- [ ] **Step 4: Register UseLocalNotification in MauiProgram**

In `ChildDev.Mobile/MauiProgram.cs`, change the builder chain from:
```csharp
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
```
to:
```csharp
        builder
            .UseMauiApp<App>()
            .UseLocalNotification()
            .ConfigureFonts(fonts =>
```

Also add `using Plugin.LocalNotification;` at the top of `MauiProgram.cs`.

- [ ] **Step 5: Add DisplayActionSheetAsync to FakeNavigationService in tests**

Open `ChildDev.Mobile.Tests/ViewModelTestBase.cs`. In the `FakeNavigationService` class, add:

```csharp
    public List<string> ActionSheetTitles { get; } = [];
    public string? ActionSheetResult { get; set; }

    public Task<string?> DisplayActionSheetAsync(string title, string cancel, string? destruction, params string[] buttons)
    {
        ActionSheetTitles.Add(title);
        return Task.FromResult(ActionSheetResult);
    }
```

- [ ] **Step 6: Build to verify**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
dotnet build ChildDev.Mobile/LevelUp.csproj /p:TargetFramework=net8.0 /p:SkipMauiTargets=true --nologo -v minimal 2>&1 | tail -10
```

Expected: `Build succeeded. 0 Error(s)`

Also run tests to confirm no regressions:
```bash
MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true --nologo 2>&1 | tail -5
```

Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add ChildDev.Mobile/LevelUp.csproj ChildDev.Mobile/Services/INavigationService.cs ChildDev.Mobile/Services/MauiNavigationService.cs ChildDev.Mobile/MauiProgram.cs ChildDev.Mobile.Tests/ViewModelTestBase.cs
git commit -m "feat: add Plugin.LocalNotification and DisplayActionSheetAsync to INavigationService"
```

---

## Task 2: Reminder model, ReminderRepository, LocalDatabase

**Files:**
- Create: `ChildDev.Mobile/Models/Reminder.cs`
- Create: `ChildDev.Mobile/Data/ReminderRepository.cs`
- Modify: `ChildDev.Mobile/Data/LocalDatabase.cs`

- [ ] **Step 1: Create Reminder model**

Create `ChildDev.Mobile/Models/Reminder.cs`:

```csharp
using SQLite;

namespace LevelUp.Models;

public class Reminder
{
    [PrimaryKey]
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    [Indexed]
    public string AccountFk { get; set; } = string.Empty;
    public string Topic { get; set; } = "General"; // "Goal", "Journal", "Todo", "General"
    public string? EntityGuid { get; set; }         // null for topic-level reminders
    public string Title { get; set; } = string.Empty;
    public string? EntityLabel { get; set; }        // display label, e.g. goal text snippet
    public long FireAt { get; set; }                // Unix milliseconds
    public bool IsDismissed { get; set; }
    public int NotificationId { get; set; }         // used to cancel/update the OS notification
}
```

- [ ] **Step 2: Create ReminderRepository**

Create `ChildDev.Mobile/Data/ReminderRepository.cs`:

```csharp
using LevelUp.Models;
using SQLite;

namespace LevelUp.Data;

public class ReminderRepository(SQLiteAsyncConnection db)
{
    public async Task<int> SaveAsync(Reminder reminder)
    {
        return await db.InsertOrReplaceAsync(reminder);
    }

    public Task<List<Reminder>> GetPendingAsync(string accountFk)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return db.Table<Reminder>()
            .Where(r => r.AccountFk == accountFk && !r.IsDismissed)
            .OrderBy(r => r.FireAt)
            .ToListAsync();
    }

    public Task<List<Reminder>> GetForEntityAsync(string entityGuid) =>
        db.Table<Reminder>()
            .Where(r => r.EntityGuid == entityGuid && !r.IsDismissed)
            .OrderBy(r => r.FireAt)
            .ToListAsync();

    public Task<Reminder?> GetAsync(string guid) =>
        db.FindAsync<Reminder>(guid);

    public Task DeleteAsync(string guid) =>
        db.DeleteAsync<Reminder>(guid);
}
```

- [ ] **Step 3: Add Reminder table to LocalDatabase**

In `ChildDev.Mobile/Data/LocalDatabase.cs`, add one line to `InitAsync()`:

Change:
```csharp
        await _db.CreateTableAsync<Todo>();
    }
```
To:
```csharp
        await _db.CreateTableAsync<Todo>();
        await _db.CreateTableAsync<Reminder>();
    }
```

- [ ] **Step 4: Build to verify**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
dotnet build ChildDev.Mobile/LevelUp.csproj /p:TargetFramework=net8.0 /p:SkipMauiTargets=true --nologo -v minimal 2>&1 | tail -10
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add ChildDev.Mobile/Models/Reminder.cs ChildDev.Mobile/Data/ReminderRepository.cs ChildDev.Mobile/Data/LocalDatabase.cs
git commit -m "feat: add Reminder model, ReminderRepository, and table creation"
```

---

## Task 3: INotificationService, MauiNotificationService, ReminderService, SnoozeHelper

**Files:**
- Create: `ChildDev.Mobile/Services/INotificationService.cs`
- Create: `ChildDev.Mobile/Services/MauiNotificationService.cs`
- Create: `ChildDev.Mobile/Services/ReminderService.cs`
- Create: `ChildDev.Mobile/Services/SnoozeHelper.cs`

- [ ] **Step 1: Create INotificationService**

Create `ChildDev.Mobile/Services/INotificationService.cs`:

```csharp
namespace LevelUp.Services;

public interface INotificationService
{
    Task<bool> RequestPermissionAsync();
    Task ScheduleAsync(int id, string title, string body, DateTime fireAt, string returningData);
    Task CancelAsync(int id);
}
```

- [ ] **Step 2: Create MauiNotificationService**

Create `ChildDev.Mobile/Services/MauiNotificationService.cs`:

```csharp
namespace LevelUp.Services;

public class MauiNotificationService : INotificationService
{
#if ANDROID || IOS || MACCATALYST || WINDOWS
    public async Task<bool> RequestPermissionAsync()
    {
        var result = await Plugin.LocalNotification.LocalNotificationCenter.Current.RequestNotificationPermission();
        return result;
    }

    public async Task ScheduleAsync(int id, string title, string body, DateTime fireAt, string returningData)
    {
        var request = new Plugin.LocalNotification.NotificationRequest
        {
            NotificationId = id,
            Title = title,
            Description = body,
            ReturningData = returningData,
            Schedule = new Plugin.LocalNotification.NotificationRequestSchedule
            {
                NotifyTime = fireAt,
            }
        };
        await Plugin.LocalNotification.LocalNotificationCenter.Current.Show(request);
    }

    public Task CancelAsync(int id)
    {
        Plugin.LocalNotification.LocalNotificationCenter.Current.Cancel(id);
        return Task.CompletedTask;
    }
#else
    public Task<bool> RequestPermissionAsync() => Task.FromResult(true);
    public Task ScheduleAsync(int id, string title, string body, DateTime fireAt, string returningData) => Task.CompletedTask;
    public Task CancelAsync(int id) => Task.CompletedTask;
#endif
}
```

- [ ] **Step 3: Create SnoozeHelper**

Create `ChildDev.Mobile/Services/SnoozeHelper.cs`:

```csharp
namespace LevelUp.Services;

public static class SnoozeHelper
{
    public static async Task<TimeSpan?> PickAsync(INavigationService nav)
    {
        var choice = await nav.DisplayActionSheetAsync(
            "Remind me in...", "Cancel", null,
            "1 hour", "8 hours", "1 day", "3 days", "Custom...");

        return choice switch
        {
            "1 hour" => TimeSpan.FromHours(1),
            "8 hours" => TimeSpan.FromHours(8),
            "1 day" => TimeSpan.FromDays(1),
            "3 days" => TimeSpan.FromDays(3),
            "Custom..." => await PickCustomAsync(nav),
            _ => null
        };
    }

    private static async Task<TimeSpan?> PickCustomAsync(INavigationService nav)
    {
        var amountStr = await nav.DisplayPromptAsync(
            "Custom Reminder", "How many?", "OK", "Cancel", "e.g. 2", 4);
        if (amountStr is null || !int.TryParse(amountStr, out int amount) || amount <= 0)
            return null;

        var unit = await nav.DisplayActionSheetAsync(
            "Choose unit", "Cancel", null, "Hours", "Days", "Weeks", "Months");

        return unit switch
        {
            "Hours" => TimeSpan.FromHours(amount),
            "Days" => TimeSpan.FromDays(amount),
            "Weeks" => TimeSpan.FromDays(amount * 7),
            "Months" => TimeSpan.FromDays(amount * 30),
            _ => null
        };
    }
}
```

- [ ] **Step 4: Create ReminderService**

Create `ChildDev.Mobile/Services/ReminderService.cs`:

```csharp
using LevelUp.Data;
using LevelUp.Models;

namespace LevelUp.Services;

public class ReminderService(ReminderRepository repo, INotificationService notifications)
{
    public async Task ScheduleAsync(Reminder reminder)
    {
        reminder.NotificationId = Math.Abs(reminder.Guid.GetHashCode()) % 1_000_000;
        await repo.SaveAsync(reminder);
        await notifications.RequestPermissionAsync();
        var fireAt = DateTimeOffset.FromUnixTimeMilliseconds(reminder.FireAt).LocalDateTime;
        await notifications.ScheduleAsync(
            reminder.NotificationId,
            reminder.Title,
            reminder.EntityLabel ?? reminder.Topic,
            fireAt,
            reminder.Guid);
    }

    public async Task SnoozeAsync(Reminder reminder, TimeSpan duration)
    {
        await notifications.CancelAsync(reminder.NotificationId);
        reminder.FireAt = DateTimeOffset.UtcNow.Add(duration).ToUnixTimeMilliseconds();
        await ScheduleAsync(reminder);
    }

    public async Task DismissAsync(Reminder reminder)
    {
        await notifications.CancelAsync(reminder.NotificationId);
        reminder.IsDismissed = true;
        await repo.SaveAsync(reminder);
    }

    public Task<List<Reminder>> GetPendingAsync(string accountFk) =>
        repo.GetPendingAsync(accountFk);

    public Task<List<Reminder>> GetForEntityAsync(string entityGuid) =>
        repo.GetForEntityAsync(entityGuid);
}
```

- [ ] **Step 5: Build to verify**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
dotnet build ChildDev.Mobile/LevelUp.csproj /p:TargetFramework=net8.0 /p:SkipMauiTargets=true --nologo -v minimal 2>&1 | tail -10
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add ChildDev.Mobile/Services/INotificationService.cs ChildDev.Mobile/Services/MauiNotificationService.cs ChildDev.Mobile/Services/ReminderService.cs ChildDev.Mobile/Services/SnoozeHelper.cs
git commit -m "feat: add INotificationService, MauiNotificationService, ReminderService, SnoozeHelper"
```

---

## Task 4: ReminderService unit tests

**Files:**
- Modify: `ChildDev.Mobile.Tests/ViewModelTestBase.cs`
- Create: `ChildDev.Mobile.Tests/ReminderServiceTests.cs`

- [ ] **Step 1: Add FakeNotificationService and reminder infrastructure to ViewModelTestBase**

In `ChildDev.Mobile.Tests/ViewModelTestBase.cs`, add the following fields and constructor changes to `ViewModelTestBase`:

After the existing `protected readonly MobileAnalyticsService Analytics;` line, add:
```csharp
    protected readonly ReminderRepository ReminderRepo;
    protected readonly FakeNotificationService NotificationService;
    protected readonly ReminderService ReminderSvc;
```

In the constructor, after `Nav = new FakeNavigationService();`, add:
```csharp
        Db.CreateTableAsync<LevelUp.Models.Reminder>().GetAwaiter().GetResult();
        ReminderRepo = new ReminderRepository(Db);
        NotificationService = new FakeNotificationService();
        ReminderSvc = new ReminderService(ReminderRepo, NotificationService);
```

At the bottom of the file (after `NoOpHttpHandler`), add:

```csharp
public class FakeNotificationService : INotificationService
{
    public record ScheduledNotification(int Id, string Title, string Body, DateTime FireAt, string Data);
    public List<ScheduledNotification> Scheduled { get; } = [];
    public List<int> Cancelled { get; } = [];

    public Task<bool> RequestPermissionAsync() => Task.FromResult(true);

    public Task ScheduleAsync(int id, string title, string body, DateTime fireAt, string returningData)
    {
        Scheduled.Add(new ScheduledNotification(id, title, body, fireAt, returningData));
        return Task.CompletedTask;
    }

    public Task CancelAsync(int id)
    {
        Cancelled.Add(id);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Create ReminderServiceTests.cs**

Create `ChildDev.Mobile.Tests/ReminderServiceTests.cs`:

```csharp
using LevelUp.Models;
using LevelUp.Services;

namespace LevelUp.Tests;

public class ReminderServiceTests : ViewModelTestBase
{
    private Reminder BuildReminder(string accountFk, string title = "Test Reminder", string topic = "General") =>
        new()
        {
            AccountFk = accountFk,
            Title = title,
            Topic = topic,
            FireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
        };

    [Fact]
    public async Task Schedule_SavesReminderToDb()
    {
        var account = await CreateTestAccountAsync();
        var reminder = BuildReminder(account.Guid, "Check goals");

        await ReminderSvc.ScheduleAsync(reminder);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.Equal("Check goals", pending[0].Title);
    }

    [Fact]
    public async Task Schedule_SchedulesOsNotification()
    {
        var account = await CreateTestAccountAsync();
        var reminder = BuildReminder(account.Guid, "Journal reminder");

        await ReminderSvc.ScheduleAsync(reminder);

        Assert.Single(NotificationService.Scheduled);
        Assert.Equal("Journal reminder", NotificationService.Scheduled[0].Title);
    }

    [Fact]
    public async Task Schedule_AssignsNotificationId()
    {
        var account = await CreateTestAccountAsync();
        var reminder = BuildReminder(account.Guid);

        await ReminderSvc.ScheduleAsync(reminder);

        Assert.NotEqual(0, reminder.NotificationId);
    }

    [Fact]
    public async Task Schedule_StoresReturningDataAsReminderGuid()
    {
        var account = await CreateTestAccountAsync();
        var reminder = BuildReminder(account.Guid);

        await ReminderSvc.ScheduleAsync(reminder);

        Assert.Equal(reminder.Guid, NotificationService.Scheduled[0].Data);
    }

    [Fact]
    public async Task Snooze_CancelsOldNotificationAndReschedulesNew()
    {
        var account = await CreateTestAccountAsync();
        var reminder = BuildReminder(account.Guid);
        await ReminderSvc.ScheduleAsync(reminder);
        var originalId = reminder.NotificationId;
        NotificationService.Scheduled.Clear();

        await ReminderSvc.SnoozeAsync(reminder, TimeSpan.FromHours(1));

        Assert.Contains(originalId, NotificationService.Cancelled);
        Assert.Single(NotificationService.Scheduled);
    }

    [Fact]
    public async Task Snooze_UpdatesFireAt()
    {
        var account = await CreateTestAccountAsync();
        var reminder = BuildReminder(account.Guid);
        var originalFireAt = reminder.FireAt;
        await ReminderSvc.ScheduleAsync(reminder);

        await ReminderSvc.SnoozeAsync(reminder, TimeSpan.FromHours(8));

        Assert.True(reminder.FireAt > originalFireAt);
        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
    }

    [Fact]
    public async Task Dismiss_CancelsNotificationAndMarksDismissed()
    {
        var account = await CreateTestAccountAsync();
        var reminder = BuildReminder(account.Guid);
        await ReminderSvc.ScheduleAsync(reminder);

        await ReminderSvc.DismissAsync(reminder);

        Assert.Contains(reminder.NotificationId, NotificationService.Cancelled);
        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task GetPending_ExcludesDismissed()
    {
        var account = await CreateTestAccountAsync();
        var r1 = BuildReminder(account.Guid, "Active");
        var r2 = BuildReminder(account.Guid, "Dismissed");
        await ReminderSvc.ScheduleAsync(r1);
        await ReminderSvc.ScheduleAsync(r2);
        await ReminderSvc.DismissAsync(r2);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.Equal("Active", pending[0].Title);
    }

    [Fact]
    public async Task GetForEntity_ReturnsOnlyMatchingEntityReminders()
    {
        var account = await CreateTestAccountAsync();
        var goalGuid = System.Guid.NewGuid().ToString();
        var goalReminder = new Reminder
        {
            AccountFk = account.Guid,
            Title = "Goal reminder",
            Topic = "Goal",
            EntityGuid = goalGuid,
            FireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
        };
        var generalReminder = BuildReminder(account.Guid, "General");
        await ReminderSvc.ScheduleAsync(goalReminder);
        await ReminderSvc.ScheduleAsync(generalReminder);

        var forEntity = await ReminderSvc.GetForEntityAsync(goalGuid);
        Assert.Single(forEntity);
        Assert.Equal("Goal reminder", forEntity[0].Title);
    }
}
```

- [ ] **Step 3: Run tests**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true --nologo --filter "ReminderServiceTests" 2>&1 | tail -20
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add ChildDev.Mobile.Tests/ViewModelTestBase.cs ChildDev.Mobile.Tests/ReminderServiceTests.cs
git commit -m "test: add ReminderService unit tests and FakeNotificationService"
```

---

## Task 5: RemindersViewModel and unit tests

**Files:**
- Create: `ChildDev.Mobile/ViewModels/RemindersViewModel.cs`
- Create: `ChildDev.Mobile.Tests/RemindersViewModelTests.cs`
- Modify: `ChildDev.Mobile/MauiProgram.cs`

- [ ] **Step 1: Create RemindersViewModel**

Create `ChildDev.Mobile/ViewModels/RemindersViewModel.cs`:

```csharp
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
```

- [ ] **Step 2: Register RemindersViewModel and dependencies in MauiProgram.cs**

In `ChildDev.Mobile/MauiProgram.cs`, add after `builder.Services.AddSingleton<INavigationService, MauiNavigationService>();`:

```csharp
        builder.Services.AddSingleton<INotificationService, MauiNotificationService>();
        builder.Services.AddSingleton<ReminderRepository>();
        builder.Services.AddSingleton<ReminderService>();
```

And after the existing `builder.Services.AddTransient<SettingsViewModel>();` line:
```csharp
        builder.Services.AddTransient<RemindersViewModel>();
```

Add these `using` statements at the top of `MauiProgram.cs`:
```csharp
using LevelUp.Services;
```
(If not already present.)

- [ ] **Step 3: Build to verify**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
dotnet build ChildDev.Mobile/LevelUp.csproj /p:TargetFramework=net8.0 /p:SkipMauiTargets=true --nologo -v minimal 2>&1 | tail -10
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Create RemindersViewModelTests.cs**

Create `ChildDev.Mobile.Tests/RemindersViewModelTests.cs`:

```csharp
using LevelUp.Models;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

public class RemindersViewModelTests : ViewModelTestBase
{
    private RemindersViewModel BuildVm() =>
        new(ReminderSvc, AccountService, Nav);

    [Fact]
    public async Task Load_WithNoPendingReminders_HasRemindersIsFalse()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Empty(vm.Reminders);
        Assert.False(vm.HasReminders);
    }

    [Fact]
    public async Task Load_PopulatesPendingReminders()
    {
        var account = await CreateTestAccountAsync();
        var reminder = new Reminder
        {
            AccountFk = account.Guid,
            Title = "Check goals",
            Topic = "Goal",
            FireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
        };
        await ReminderSvc.ScheduleAsync(reminder);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Single(vm.Reminders);
        Assert.True(vm.HasReminders);
        Assert.Equal("Check goals", vm.Reminders[0].Title);
    }

    [Fact]
    public async Task Dismiss_RemovesReminderFromList()
    {
        var account = await CreateTestAccountAsync();
        var reminder = new Reminder
        {
            AccountFk = account.Guid,
            Title = "Test",
            Topic = "General",
            FireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
        };
        await ReminderSvc.ScheduleAsync(reminder);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.DismissCommand.ExecuteAsync(vm.Reminders[0]);

        Assert.Empty(vm.Reminders);
        Assert.False(vm.HasReminders);
    }

    [Fact]
    public async Task Snooze_PickerCancelled_DoesNotChangeReminder()
    {
        var account = await CreateTestAccountAsync();
        var reminder = new Reminder
        {
            AccountFk = account.Guid,
            Title = "Test",
            Topic = "General",
            FireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
        };
        await ReminderSvc.ScheduleAsync(reminder);
        var originalFireAt = reminder.FireAt;

        Nav.ActionSheetResult = null; // user cancels
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.SnoozeCommand.ExecuteAsync(vm.Reminders[0]);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.Equal(originalFireAt, pending[0].FireAt);
    }

    [Fact]
    public async Task Snooze_1Hour_UpdatesFireAt()
    {
        var account = await CreateTestAccountAsync();
        var reminder = new Reminder
        {
            AccountFk = account.Guid,
            Title = "Test",
            Topic = "General",
            FireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
        };
        await ReminderSvc.ScheduleAsync(reminder);
        var originalFireAt = reminder.FireAt;

        Nav.ActionSheetResult = "1 hour";
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.SnoozeCommand.ExecuteAsync(vm.Reminders[0]);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.True(pending[0].FireAt > originalFireAt);
    }

    [Fact]
    public void AddGeneral_CanExecute_FalseWhenTitleEmpty()
    {
        var vm = BuildVm();
        vm.NewReminderTitle = string.Empty;
        Assert.False(vm.AddGeneralCommand.CanExecute(null));
    }

    [Fact]
    public void AddGeneral_CanExecute_TrueWhenTitleSet()
    {
        var vm = BuildVm();
        vm.NewReminderTitle = "Remember to practice";
        Assert.True(vm.AddGeneralCommand.CanExecute(null));
    }

    [Fact]
    public async Task AddGeneral_Confirmed_CreatesReminderAndClearsTitle()
    {
        var account = await CreateTestAccountAsync();
        Nav.ActionSheetResult = "1 day";

        var vm = BuildVm();
        vm.NewReminderTitle = "Practice guitar";
        await vm.AddGeneralCommand.ExecuteAsync(null);

        Assert.Empty(vm.NewReminderTitle);
        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.Equal("Practice guitar", pending[0].Title);
        Assert.Equal("General", pending[0].Topic);
    }
}
```

- [ ] **Step 5: Run tests**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true --nologo --filter "RemindersViewModelTests" 2>&1 | tail -20
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add ChildDev.Mobile/ViewModels/RemindersViewModel.cs ChildDev.Mobile/MauiProgram.cs ChildDev.Mobile.Tests/RemindersViewModelTests.cs
git commit -m "feat: add RemindersViewModel with load/snooze/dismiss/add-general commands"
```

---

## Task 6: RemindersPage XAML, AppShell route, notification tap handler

**Files:**
- Create: `ChildDev.Mobile/Views/RemindersPage.xaml`
- Create: `ChildDev.Mobile/Views/RemindersPage.xaml.cs`
- Modify: `ChildDev.Mobile/AppShell.xaml.cs`
- Modify: `ChildDev.Mobile/App.xaml.cs`
- Modify: `ChildDev.Mobile/MauiProgram.cs`

- [ ] **Step 1: Create RemindersPage.xaml**

Create `ChildDev.Mobile/Views/RemindersPage.xaml`:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:LevelUp.ViewModels"
             x:DataType="vm:RemindersViewModel"
             x:Class="LevelUp.Views.RemindersPage"
             Title="Reminders">

    <Grid RowDefinitions="Auto,*,Auto" Padding="16">

        <!-- Add general reminder -->
        <Grid Grid.Row="0" ColumnDefinitions="*,Auto" Margin="0,0,0,12">
            <Entry Grid.Column="0"
                   Placeholder="New reminder title..."
                   Text="{Binding NewReminderTitle}"
                   ReturnCommand="{Binding AddGeneralCommand}"
                   VerticalOptions="Center"/>
            <Button Grid.Column="1"
                    Text="+ Add"
                    Command="{Binding AddGeneralCommand}"
                    Margin="8,0,0,0"/>
        </Grid>

        <!-- Reminder list -->
        <CollectionView Grid.Row="1"
                        ItemsSource="{Binding Reminders}"
                        EmptyView="No pending reminders">
            <CollectionView.ItemTemplate>
                <DataTemplate x:DataType="x:Type TypeArguments='vm:RemindersViewModel'">
                    <SwipeView>
                        <SwipeView.RightItems>
                            <SwipeItems>
                                <SwipeItem Text="Snooze"
                                           BackgroundColor="Orange"
                                           Command="{Binding Source={RelativeSource AncestorType={x:Type vm:RemindersViewModel}}, Path=SnoozeCommand}"
                                           CommandParameter="{Binding .}"/>
                                <SwipeItem Text="Dismiss"
                                           BackgroundColor="Red"
                                           IsDestructive="True"
                                           Command="{Binding Source={RelativeSource AncestorType={x:Type vm:RemindersViewModel}}, Path=DismissCommand}"
                                           CommandParameter="{Binding .}"/>
                            </SwipeItems>
                        </SwipeView.RightItems>
                        <Grid Padding="0,8" ColumnDefinitions="*,Auto">
                            <StackLayout Grid.Column="0">
                                <Label Text="{Binding Title}" FontSize="16" FontAttributes="Bold"/>
                                <Label Text="{Binding EntityLabel}" FontSize="13" TextColor="Gray"
                                       IsVisible="{Binding EntityLabel, Converter={StaticResource NotNullConverter}}"/>
                                <Label FontSize="12" TextColor="Gray">
                                    <Label.FormattedText>
                                        <FormattedString>
                                            <Span Text="{Binding Topic}"/>
                                            <Span Text=" · "/>
                                            <Span Text="{Binding FireAt, StringFormat='{0}'}"/>
                                        </FormattedString>
                                    </Label.FormattedText>
                                </Label>
                            </StackLayout>
                        </Grid>
                    </SwipeView>
                </DataTemplate>
            </CollectionView.ItemTemplate>
        </CollectionView>

        <!-- Loading indicator -->
        <ActivityIndicator Grid.Row="1"
                           IsRunning="{Binding IsLoading}"
                           IsVisible="{Binding IsLoading}"
                           HorizontalOptions="Center"
                           VerticalOptions="Center"/>
    </Grid>
</ContentPage>
```

- [ ] **Step 2: Create RemindersPage.xaml.cs**

Create `ChildDev.Mobile/Views/RemindersPage.xaml.cs`:

```csharp
using LevelUp.ViewModels;

namespace LevelUp.Views;

public partial class RemindersPage : ContentPage
{
    public RemindersPage(RemindersViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is RemindersViewModel vm)
            vm.LoadCommand.Execute(null);
    }
}
```

- [ ] **Step 3: Register route in AppShell.xaml.cs**

In `ChildDev.Mobile/AppShell.xaml.cs`, add to the constructor after the existing `Routing.RegisterRoute` calls:

```csharp
        Routing.RegisterRoute("reminders", typeof(Views.RemindersPage));
```

- [ ] **Step 4: Add notification tap handler in App.xaml.cs**

Read current `ChildDev.Mobile/App.xaml.cs` first. Then add the notification tap handler. The file likely looks like:

```csharp
namespace LevelUp;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        MainPage = new AppShell();
    }
}
```

Replace with:

```csharp
using Plugin.LocalNotification;

namespace LevelUp;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        MainPage = new AppShell();
        LocalNotificationCenter.Current.NotificationActionTapped += OnNotificationTapped;
    }

    private void OnNotificationTapped(Plugin.LocalNotification.EventArgs.NotificationActionEventArgs e)
    {
        // Navigate to reminders page when notification is tapped
        // ReturningData contains the Reminder Guid — navigate to reminders for now
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Shell.Current.GoToAsync("reminders");
        });
    }
}
```

- [ ] **Step 5: Register RemindersPage in MauiProgram.cs**

In `ChildDev.Mobile/MauiProgram.cs`, add after the existing `builder.Services.AddTransient<SettingsPage>();`:

```csharp
        builder.Services.AddTransient<RemindersPage>();
```

- [ ] **Step 6: Build Android target to verify XAML compiles**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
dotnet build ChildDev.Mobile/LevelUp.csproj /p:TargetFramework=net8.0-android /p:SkipMauiTargets=false --nologo -v minimal 2>&1 | tail -20
```

Expected: `Build succeeded. 0 Error(s)` (warnings about MAUI APIs are acceptable).

If the XAML binding for SwipeItem causes compilation errors, simplify the SwipeView binding to use code-behind event handlers instead. Read the build error carefully and fix the XAML accordingly. Common issue: DataTemplate `x:DataType` for CollectionView items should point to the item type (Reminder), not the ViewModel.

**Corrected XAML DataTemplate if needed** — replace the DataTemplate with:
```xml
<CollectionView.ItemTemplate>
    <DataTemplate>
        <SwipeView>
            <SwipeView.RightItems>
                <SwipeItems>
                    <SwipeItem Text="Snooze"
                               BackgroundColor="Orange"
                               Clicked="OnSnoozeClicked"/>
                    <SwipeItem Text="Dismiss"
                               BackgroundColor="Red"
                               IsDestructive="True"
                               Clicked="OnDismissClicked"/>
                </SwipeItems>
            </SwipeView.RightItems>
            <Grid Padding="0,8">
                <StackLayout>
                    <Label Text="{Binding Title}" FontSize="16" FontAttributes="Bold"/>
                    <Label Text="{Binding EntityLabel}" FontSize="13" TextColor="Gray"/>
                    <Label Text="{Binding Topic}" FontSize="12" TextColor="Gray"/>
                </StackLayout>
            </Grid>
        </SwipeView>
    </DataTemplate>
</CollectionView.ItemTemplate>
```

And in `RemindersPage.xaml.cs`, add handlers:
```csharp
    private async void OnSnoozeClicked(object sender, EventArgs e)
    {
        if (sender is SwipeItem item && item.BindingContext is LevelUp.Models.Reminder r
            && BindingContext is RemindersViewModel vm)
            await vm.SnoozeCommand.ExecuteAsync(r);
    }

    private async void OnDismissClicked(object sender, EventArgs e)
    {
        if (sender is SwipeItem item && item.BindingContext is LevelUp.Models.Reminder r
            && BindingContext is RemindersViewModel vm)
            await vm.DismissCommand.ExecuteAsync(r);
    }
```

- [ ] **Step 7: Commit**

```bash
git add ChildDev.Mobile/Views/RemindersPage.xaml ChildDev.Mobile/Views/RemindersPage.xaml.cs ChildDev.Mobile/AppShell.xaml.cs ChildDev.Mobile/App.xaml.cs ChildDev.Mobile/MauiProgram.cs
git commit -m "feat: add RemindersPage, AppShell route, and notification tap handler"
```

---

## Task 7: SetReminderCommand in GoalEntry, TodoEntry, JournalEntry

**Files:**
- Modify: `ChildDev.Mobile/ViewModels/GoalEntryViewModel.cs`
- Modify: `ChildDev.Mobile/ViewModels/TodoEntryViewModel.cs`
- Modify: `ChildDev.Mobile/ViewModels/JournalEntryViewModel.cs`

**Pattern for each ViewModel:** Add `ReminderService reminderService` as the last constructor parameter (after `INavigationService nav`). Add a `SetReminderCommand` that calls `SnoozeHelper.PickAsync` then creates and schedules a `Reminder`.

- [ ] **Step 1: Update GoalEntryViewModel**

Add `ReminderService reminderService` as last constructor parameter to `GoalEntryViewModel`:

```csharp
public partial class GoalEntryViewModel(
    GoalRepository repo,
    GoalProgressRepository progressRepo,
    TodoRepository todoRepo,
    AccountService accountService,
    MobileAnalyticsService analytics,
    INavigationService nav,
    ReminderService reminderService) : ObservableObject
```

Add field: `private readonly ReminderService _reminderService = reminderService;`

Add command (after the existing commands in GoalEntryViewModel):

```csharp
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
```

Also update MauiProgram.cs to inject `ReminderService` into `GoalEntryViewModel` — since it's registered with DI as transient and `ReminderService` is singleton, DI handles this automatically. No code change needed in MauiProgram.cs (DI resolves constructor parameters by type).

- [ ] **Step 2: Update TodoEntryViewModel**

Add `ReminderService reminderService` as last constructor parameter:

```csharp
public partial class TodoEntryViewModel(
    TodoRepository repo,
    GoalRepository goalRepo,
    AccountService accountService,
    MobileAnalyticsService analytics,
    INavigationService nav,
    ReminderService reminderService) : ObservableObject
```

Add field: `private readonly ReminderService _reminderService = reminderService;`

Add command:

```csharp
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
            Topic = "Todo",
            EntityGuid = Guid,
            Title = $"Todo: {(Title?.Length > 40 ? Title[..40] + "…" : Title)}",
            EntityLabel = Title,
            FireAt = DateTimeOffset.UtcNow.Add(duration.Value).ToUnixTimeMilliseconds()
        };
        await _reminderService.ScheduleAsync(reminder);
        await _nav.AlertAsync("Reminder Set", $"You'll be reminded in {FormatDuration(duration.Value)}.", "OK");
    }

    private static string FormatDuration(TimeSpan d) => d.TotalDays >= 1
        ? $"{(int)d.TotalDays} day{((int)d.TotalDays == 1 ? "" : "s")}"
        : $"{(int)d.TotalHours} hour{((int)d.TotalHours == 1 ? "" : "s")}";
```

- [ ] **Step 3: Update JournalEntryViewModel**

Add `ReminderService reminderService` as last constructor parameter:

```csharp
public partial class JournalEntryViewModel(
    JournalRepository repo,
    AccountService accountService,
    MobileAnalyticsService analytics,
    INavigationService nav,
    ReminderService reminderService) : ObservableObject
```

Add field: `private readonly ReminderService _reminderService = reminderService;`

Add command:

```csharp
    [RelayCommand]
    private async Task SetReminderAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;

        var duration = await SnoozeHelper.PickAsync(_nav);
        if (duration is null) return;

        var label = string.IsNullOrWhiteSpace(Notes)
            ? "Journal entry"
            : (Notes.Length > 40 ? Notes[..40] + "…" : Notes);

        var reminder = new LevelUp.Models.Reminder
        {
            AccountFk = account.Guid,
            Topic = "Journal",
            EntityGuid = string.IsNullOrEmpty(Guid) ? null : Guid,
            Title = $"Journal: {label}",
            EntityLabel = label,
            FireAt = DateTimeOffset.UtcNow.Add(duration.Value).ToUnixTimeMilliseconds()
        };
        await _reminderService.ScheduleAsync(reminder);
        await _nav.AlertAsync("Reminder Set", $"You'll be reminded in {FormatDuration(duration.Value)}.", "OK");
    }

    private static string FormatDuration(TimeSpan d) => d.TotalDays >= 1
        ? $"{(int)d.TotalDays} day{((int)d.TotalDays == 1 ? "" : "s")}"
        : $"{(int)d.TotalHours} hour{((int)d.TotalHours == 1 ? "" : "s")}";
```

- [ ] **Step 4: Update ViewModelTestBase to inject ReminderService into entry VMs**

Since `GoalEntryViewModel`, `TodoEntryViewModel`, and `JournalEntryViewModel` now have `ReminderService` as the last parameter, the existing test files that call `new GoalEntryViewModel(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav)` will break.

Update the `BuildVm()` calls in:
- `ChildDev.Mobile.Tests/GoalEntryViewModelTests.cs`: change to `new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc)`
- `ChildDev.Mobile.Tests/TodoViewModelTests.cs`: change `BuildEntryVm()` to `new(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc)`
- `ChildDev.Mobile.Tests/JournalViewModelTests.cs`: change `BuildEntryVm()` to `new(JournalRepo, AccountService, Analytics, Nav, ReminderSvc)`
- `ChildDev.Mobile.Tests/OfflineCapabilityTests.cs`: update all `new GoalEntryViewModel(...)`, `new JournalEntryViewModel(...)` calls to include `ReminderSvc` as last arg

- [ ] **Step 5: Build and run full test suite**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true --nologo 2>&1 | tail -10
```

Expected: All tests pass (new ReminderSvc parameter is satisfied by ViewModelTestBase.ReminderSvc).

- [ ] **Step 6: Commit**

```bash
git add ChildDev.Mobile/ViewModels/GoalEntryViewModel.cs ChildDev.Mobile/ViewModels/TodoEntryViewModel.cs ChildDev.Mobile/ViewModels/JournalEntryViewModel.cs ChildDev.Mobile.Tests/GoalEntryViewModelTests.cs ChildDev.Mobile.Tests/TodoViewModelTests.cs ChildDev.Mobile.Tests/JournalViewModelTests.cs ChildDev.Mobile.Tests/OfflineCapabilityTests.cs
git commit -m "feat: add SetReminderCommand to GoalEntry, TodoEntry, JournalEntry ViewModels"
```

---

## Task 8: DashboardViewModel navigation + final test run

**Files:**
- Modify: `ChildDev.Mobile/ViewModels/DashboardViewModel.cs`

- [ ] **Step 1: Add OpenRemindersCommand to DashboardViewModel**

Read `ChildDev.Mobile/ViewModels/DashboardViewModel.cs` and find the existing `[RelayCommand]` commands. Add this command (DashboardViewModel already has `_nav`):

```csharp
    [RelayCommand]
    private Task OpenRemindersAsync() => _nav.GoToAsync("reminders");
```

- [ ] **Step 2: Run full test suite**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true --nologo 2>&1 | tail -10
```

Expected: All existing tests pass + new tests pass.

- [ ] **Step 3: Commit**

```bash
git add ChildDev.Mobile/ViewModels/DashboardViewModel.cs
git commit -m "feat: add OpenRemindersCommand to DashboardViewModel"
```

---

---

## Task 9: Web — Reminder entity, AppDbContext, WebReminderService

**Files:**
- Create: `ChildDev.Api/Models/Entities/Reminder.cs`
- Modify: `ChildDev.Api/Data/AppDbContext.cs`
- Create: `ChildDev.Api/Services/WebReminderService.cs`
- Modify: `ChildDev.Api/Program.cs`

The web app uses EF Core + MariaDB. `EnsureCreated()` manages schema (no migrations).

- [ ] **Step 1: Create Reminder entity**

Create `ChildDev.Api/Models/Entities/Reminder.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace ChildDev.Api.Models.Entities;

public class Reminder
{
    [Key]
    public int Id { get; set; }
    [Required, MaxLength(36)]
    public string AccountGuid { get; set; } = string.Empty;
    [MaxLength(20)]
    public string Topic { get; set; } = "General"; // "Goal", "Journal", "Todo", "General"
    [MaxLength(36)]
    public string? EntityGuid { get; set; }
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;
    [MaxLength(200)]
    public string? EntityLabel { get; set; }
    public DateTime FireAt { get; set; }
    public bool IsDismissed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 2: Add DbSet to AppDbContext**

In `ChildDev.Api/Data/AppDbContext.cs`, add after the existing `DbSet` properties:

```csharp
    public DbSet<Reminder> Reminders => Set<Reminder>();
```

Also add an index in `OnModelCreating`:

```csharp
        modelBuilder.Entity<Reminder>()
            .HasIndex(r => new { r.AccountGuid, r.IsDismissed });
```

- [ ] **Step 3: Create WebReminderService**

Create `ChildDev.Api/Services/WebReminderService.cs`:

```csharp
using ChildDev.Api.Data;
using ChildDev.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Services;

public class WebReminderService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<Reminder>> GetPendingAsync(string accountGuid)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Reminders
            .Where(r => r.AccountGuid == accountGuid && !r.IsDismissed)
            .OrderBy(r => r.FireAt)
            .ToListAsync();
    }

    public async Task<List<Reminder>> GetDueAsync(string accountGuid)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        return await db.Reminders
            .Where(r => r.AccountGuid == accountGuid && !r.IsDismissed && r.FireAt <= now)
            .OrderBy(r => r.FireAt)
            .ToListAsync();
    }

    public async Task<Reminder> CreateAsync(string accountGuid, string title, string topic,
        string? entityGuid, string? entityLabel, DateTime fireAt)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var reminder = new Reminder
        {
            AccountGuid = accountGuid,
            Title = title,
            Topic = topic,
            EntityGuid = entityGuid,
            EntityLabel = entityLabel,
            FireAt = fireAt
        };
        db.Reminders.Add(reminder);
        await db.SaveChangesAsync();
        return reminder;
    }

    public async Task SnoozeAsync(int id, TimeSpan duration)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var reminder = await db.Reminders.FindAsync(id);
        if (reminder is null) return;
        reminder.FireAt = DateTime.UtcNow.Add(duration);
        await db.SaveChangesAsync();
    }

    public async Task DismissAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var reminder = await db.Reminders.FindAsync(id);
        if (reminder is null) return;
        reminder.IsDismissed = true;
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: Register WebReminderService in Program.cs**

In `ChildDev.Api/Program.cs`, find where `WebAnalyticsService` is registered (e.g., `builder.Services.AddScoped<WebAnalyticsService>();`) and add after it:

```csharp
builder.Services.AddScoped<WebReminderService>();
```

- [ ] **Step 5: Build to verify**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
dotnet build ChildDev.Api/ChildDev.Api.csproj --nologo -v minimal 2>&1 | tail -10
```

Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add ChildDev.Api/Models/Entities/Reminder.cs ChildDev.Api/Data/AppDbContext.cs ChildDev.Api/Services/WebReminderService.cs ChildDev.Api/Program.cs
git commit -m "feat: add web Reminder entity, AppDbContext DbSet, and WebReminderService"
```

---

## Task 10: Web — Reminders.razor page with browser notifications and snooze

**Files:**
- Create: `ChildDev.Api/Components/Pages/Reminders.razor`
- Modify: `ChildDev.Api/wwwroot/site.js`
- Modify: `ChildDev.Api/Components/Layout/MainLayout.razor` (add nav link)

This page lists pending reminders, fires browser notifications for due ones, and supports snooze via MudDialog.

- [ ] **Step 1: Add browser notification JS to site.js**

Read `ChildDev.Api/wwwroot/site.js` first, then append these functions at the end:

```javascript
window.requestNotificationPermission = async function () {
    if (!('Notification' in window)) return 'denied';
    if (Notification.permission === 'granted') return 'granted';
    return await Notification.requestPermission();
};

window.showBrowserNotification = function (title, body) {
    if (Notification.permission === 'granted') {
        new Notification(title, { body: body, icon: '/icon.svg' });
    }
};
```

- [ ] **Step 2: Create Reminders.razor**

Create `ChildDev.Api/Components/Pages/Reminders.razor`:

```razor
@page "/reminders"
@inject IDbContextFactory<AppDbContext> DbFactory
@inject NavigationManager Nav
@inject IHttpContextAccessor HttpContextAccessor
@inject ISnackbar Snackbar
@inject WebAnalyticsService Analytics
@inject WebReminderService ReminderSvc
@inject IJSRuntime JS
@implements IAsyncDisposable

<PageTitle>Reminders – LevelUp</PageTitle>

@if (AccountGuid is null)
{
    Nav.NavigateTo("/login");
    return;
}

<MudText Typo="Typo.h5" Class="mb-4">
    <MudIcon Icon="@Icons.Material.Filled.NotificationsActive" Class="mr-2" />
    Reminders
</MudText>

@* Add new reminder *@
<MudPaper Elevation="1" Class="pa-4 mb-4" Style="border-radius:12px">
    <MudText Typo="Typo.subtitle1" Class="mb-2">New Reminder</MudText>
    <MudGrid>
        <MudItem xs="12" sm="6">
            <MudTextField @bind-Value="_newTitle" Label="Title" Placeholder="What do you want to be reminded about?"
                          Variant="Variant.Outlined" MaxLength="200" />
        </MudItem>
        <MudItem xs="6" sm="3">
            <MudSelect @bind-Value="_newTopic" Label="Topic" Variant="Variant.Outlined">
                <MudSelectItem Value="@("General")">General</MudSelectItem>
                <MudSelectItem Value="@("Goal")">Goal</MudSelectItem>
                <MudSelectItem Value="@("Journal")">Journal</MudSelectItem>
                <MudSelectItem Value="@("Todo")">Todo</MudSelectItem>
            </MudSelect>
        </MudItem>
        <MudItem xs="6" sm="3">
            <MudSelect @bind-Value="_snoozePreset" Label="Remind me in" Variant="Variant.Outlined">
                <MudSelectItem Value="@("1h")">1 hour</MudSelectItem>
                <MudSelectItem Value="@("8h")">8 hours</MudSelectItem>
                <MudSelectItem Value="@("1d")">1 day</MudSelectItem>
                <MudSelectItem Value="@("3d")">3 days</MudSelectItem>
                <MudSelectItem Value="@("custom")">Custom...</MudSelectItem>
            </MudSelect>
        </MudItem>
        @if (_snoozePreset == "custom")
        {
            <MudItem xs="6" sm="3">
                <MudNumericField @bind-Value="_customAmount" Label="Amount" Min="1" Max="999" Variant="Variant.Outlined" />
            </MudItem>
            <MudItem xs="6" sm="3">
                <MudSelect @bind-Value="_customUnit" Label="Unit" Variant="Variant.Outlined">
                    <MudSelectItem Value="@("hours")">Hours</MudSelectItem>
                    <MudSelectItem Value="@("days")">Days</MudSelectItem>
                    <MudSelectItem Value="@("weeks")">Weeks</MudSelectItem>
                    <MudSelectItem Value="@("months")">Months</MudSelectItem>
                </MudSelect>
            </MudItem>
        }
        <MudItem xs="12">
            <MudButton Variant="Variant.Filled" Color="Color.Primary"
                       OnClick="AddReminderAsync"
                       Disabled="@(string.IsNullOrWhiteSpace(_newTitle))">
                Set Reminder
            </MudButton>
        </MudItem>
    </MudGrid>
</MudPaper>

@* Pending reminders list *@
@if (_reminders.Count == 0)
{
    <MudText Color="Color.Secondary">No pending reminders.</MudText>
}
else
{
    <MudText Typo="Typo.subtitle1" Class="mb-2">Pending (@_reminders.Count)</MudText>
    @foreach (var r in _reminders)
    {
        <MudPaper Elevation="1" Class="pa-3 mb-2" Style="border-radius:10px">
            <MudStack Row="true" AlignItems="AlignItems.Center" Wrap="Wrap.Wrap" Spacing="2">
                <MudStack Style="flex:1" Spacing="0">
                    <MudText Typo="Typo.body1" Style="font-weight:500">@r.Title</MudText>
                    @if (!string.IsNullOrEmpty(r.EntityLabel))
                    {
                        <MudText Typo="Typo.caption" Color="Color.Secondary">@r.EntityLabel</MudText>
                    }
                    <MudText Typo="Typo.caption" Color="Color.Secondary">
                        @r.Topic · @r.FireAt.ToLocalTime().ToString("MMM d, h:mm tt")
                        @if (r.FireAt <= DateTime.UtcNow)
                        {
                            <MudChip Size="Size.Small" Color="Color.Warning" Class="ml-1">Due</MudChip>
                        }
                    </MudText>
                </MudStack>
                <MudButtonGroup Variant="Variant.Outlined" Size="Size.Small">
                    <MudButton OnClick="() => OpenSnoozeDialog(r)">Snooze</MudButton>
                    <MudButton Color="Color.Error" OnClick="() => DismissAsync(r)">Dismiss</MudButton>
                </MudButtonGroup>
            </MudStack>
        </MudPaper>
    }
}

@* Snooze dialog *@
<MudDialog @bind-Visible="_snoozeOpen">
    <TitleContent>Snooze Reminder</TitleContent>
    <DialogContent>
        <MudSelect @bind-Value="_snoozeDialogPreset" Label="Snooze for" Variant="Variant.Outlined" Class="mb-2">
            <MudSelectItem Value="@("1h")">1 hour</MudSelectItem>
            <MudSelectItem Value="@("8h")">8 hours</MudSelectItem>
            <MudSelectItem Value="@("1d")">1 day</MudSelectItem>
            <MudSelectItem Value="@("3d")">3 days</MudSelectItem>
            <MudSelectItem Value="@("custom")">Custom...</MudSelectItem>
        </MudSelect>
        @if (_snoozeDialogPreset == "custom")
        {
            <MudStack Row="true" Spacing="2">
                <MudNumericField @bind-Value="_snoozeCustomAmount" Label="Amount" Min="1" Max="999" Variant="Variant.Outlined" />
                <MudSelect @bind-Value="_snoozeCustomUnit" Label="Unit" Variant="Variant.Outlined">
                    <MudSelectItem Value="@("hours")">Hours</MudSelectItem>
                    <MudSelectItem Value="@("days")">Days</MudSelectItem>
                    <MudSelectItem Value="@("weeks")">Weeks</MudSelectItem>
                    <MudSelectItem Value="@("months")">Months</MudSelectItem>
                </MudSelect>
            </MudStack>
        }
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="() => _snoozeOpen = false">Cancel</MudButton>
        <MudButton Color="Color.Primary" OnClick="ConfirmSnoozeAsync">Snooze</MudButton>
    </DialogActions>
</MudDialog>

@code {
    private string? AccountGuid;
    private List<Reminder> _reminders = [];

    // New reminder form
    private string _newTitle = string.Empty;
    private string _newTopic = "General";
    private string _snoozePreset = "1h";
    private int _customAmount = 1;
    private string _customUnit = "hours";

    // Snooze dialog
    private bool _snoozeOpen;
    private Reminder? _snoozeTarget;
    private string _snoozeDialogPreset = "1h";
    private int _snoozeCustomAmount = 1;
    private string _snoozeCustomUnit = "hours";

    private System.Timers.Timer? _dueCheckTimer;

    protected override async Task OnInitializedAsync()
    {
        AccountGuid = HttpContextAccessor.HttpContext?.Session.GetString("AccountGuid");
        if (AccountGuid is null) return;
        await LoadAsync();
        await Analytics.TrackAsync(AccountGuid, "page_view", "reminders");
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        // Request browser notification permission
        await JS.InvokeAsync<string>("requestNotificationPermission");
        // Fire browser notifications for any due reminders
        await FireDueNotificationsAsync();
        // Check every 60 seconds for new due reminders
        _dueCheckTimer = new System.Timers.Timer(60_000);
        _dueCheckTimer.Elapsed += async (_, _) => await InvokeAsync(FireDueNotificationsAsync);
        _dueCheckTimer.Start();
    }

    private async Task LoadAsync()
    {
        if (AccountGuid is null) return;
        _reminders = await ReminderSvc.GetPendingAsync(AccountGuid);
    }

    private async Task FireDueNotificationsAsync()
    {
        if (AccountGuid is null) return;
        var due = await ReminderSvc.GetDueAsync(AccountGuid);
        foreach (var r in due)
        {
            await JS.InvokeVoidAsync("showBrowserNotification", r.Title, r.EntityLabel ?? r.Topic);
        }
    }

    private async Task AddReminderAsync()
    {
        if (string.IsNullOrWhiteSpace(_newTitle) || AccountGuid is null) return;
        var duration = ParsePreset(_snoozePreset, _customAmount, _customUnit);
        var fireAt = DateTime.UtcNow.Add(duration);
        await ReminderSvc.CreateAsync(AccountGuid, _newTitle.Trim(), _newTopic, null, null, fireAt);
        _newTitle = string.Empty;
        Snackbar.Add("Reminder set!", Severity.Success);
        await LoadAsync();
        await Analytics.TrackAsync(AccountGuid, "reminder_created", "reminders");
    }

    private void OpenSnoozeDialog(Reminder r)
    {
        _snoozeTarget = r;
        _snoozeDialogPreset = "1h";
        _snoozeCustomAmount = 1;
        _snoozeCustomUnit = "hours";
        _snoozeOpen = true;
    }

    private async Task ConfirmSnoozeAsync()
    {
        if (_snoozeTarget is null) return;
        var duration = ParsePreset(_snoozeDialogPreset, _snoozeCustomAmount, _snoozeCustomUnit);
        await ReminderSvc.SnoozeAsync(_snoozeTarget.Id, duration);
        _snoozeOpen = false;
        Snackbar.Add("Snoozed!", Severity.Info);
        await LoadAsync();
    }

    private async Task DismissAsync(Reminder r)
    {
        await ReminderSvc.DismissAsync(r.Id);
        _reminders.Remove(r);
        Snackbar.Add("Reminder dismissed.", Severity.Normal);
    }

    private static TimeSpan ParsePreset(string preset, int amount, string unit) => preset switch
    {
        "1h" => TimeSpan.FromHours(1),
        "8h" => TimeSpan.FromHours(8),
        "1d" => TimeSpan.FromDays(1),
        "3d" => TimeSpan.FromDays(3),
        "custom" => unit switch
        {
            "hours" => TimeSpan.FromHours(amount),
            "days" => TimeSpan.FromDays(amount),
            "weeks" => TimeSpan.FromDays(amount * 7),
            "months" => TimeSpan.FromDays(amount * 30),
            _ => TimeSpan.FromHours(1)
        },
        _ => TimeSpan.FromHours(1)
    };

    public async ValueTask DisposeAsync()
    {
        _dueCheckTimer?.Stop();
        _dueCheckTimer?.Dispose();
    }
}
```

- [ ] **Step 3: Add Reminders nav link in MainLayout.razor**

In `ChildDev.Api/Components/Layout/MainLayout.razor`, find the navigation drawer section where other nav items like Goals, Todos, Journal are listed, and add a Reminders item. Look for a pattern like `<MudNavLink Href="/todos" ...>` and add after the Todos entry:

```razor
<MudNavLink Href="/reminders" Icon="@Icons.Material.Filled.NotificationsActive" Match="NavLinkMatch.Prefix">Reminders</MudNavLink>
```

- [ ] **Step 4: Build the API project**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
dotnet build ChildDev.Api/ChildDev.Api.csproj --nologo -v minimal 2>&1 | tail -10
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add ChildDev.Api/Components/Pages/Reminders.razor ChildDev.Api/wwwroot/site.js ChildDev.Api/Components/Layout/MainLayout.razor
git commit -m "feat: add web Reminders page with browser notifications and snooze"
```

---

## Self-Review

**Spec coverage:**
- ✅ Local notifications via Plugin.LocalNotification
- ✅ Reminder model stored in SQLite (device-local)
- ✅ INotificationService abstraction for testability
- ✅ ReminderService: schedule, snooze, dismiss, get-pending
- ✅ SnoozeHelper with 1h/8h/1d/3d/custom durations
- ✅ Custom duration: two-step prompt (amount + unit: hours/days/weeks/months)
- ✅ RemindersViewModel + RemindersPage
- ✅ Per-entity SetReminderCommand on GoalEntry, TodoEntry, JournalEntry
- ✅ Topic-level (General) reminders from RemindersPage
- ✅ Notification tap → navigate to reminders page
- ✅ Unit tests for ReminderService and RemindersViewModel
- ✅ DashboardViewModel → OpenRemindersCommand

**No placeholders detected.**

**Type consistency:**
- `ReminderService(ReminderRepository, INotificationService)` — consistent throughout
- `SnoozeHelper.PickAsync(INavigationService)` returns `Task<TimeSpan?>` — consistent
- `Reminder.FireAt` is `long` (Unix ms) — consistent with all other date fields in the app
- `FakeNotificationService` implements `INotificationService` exactly
