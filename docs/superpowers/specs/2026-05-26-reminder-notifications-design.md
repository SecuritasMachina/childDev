# Reminder Notifications Design

**Date:** 2026-05-26

## Goal

Add local push notifications to the LevelUp mobile app so users can schedule reminders for goals, todos, journals, or general topics. When a notification fires, the user can snooze it by 1h, 8h, 1d, 3d, or a custom duration (user-entered hours/days/weeks/months).

## Architecture

Local notifications via `Plugin.LocalNotification` (standard MAUI Android package). Reminders are stored in SQLite (device-local — no server sync required). A thin `INotificationService` abstraction wraps the plugin for testability. `ReminderService` orchestrates saving to DB + scheduling the notification. A `RemindersPage` lists all pending reminders. Per-entity reminders are created from GoalEntry, TodoEntry, and JournalEntry pages. Topic-level reminders (not tied to a specific entry) are created from the RemindersPage.

## Data Model

```csharp
// ChildDev.Mobile/Models/Reminder.cs
public class Reminder
{
    [PrimaryKey]
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    [Indexed]
    public string AccountFk { get; set; } = string.Empty;
    public string Topic { get; set; } = "General"; // "Goal", "Journal", "Todo", "General"
    public string? EntityGuid { get; set; }         // null for topic-level reminders
    public string Title { get; set; } = string.Empty;
    public string? EntityLabel { get; set; }        // e.g. goal text snippet for display
    public long FireAt { get; set; }                // Unix ms, when notification fires
    public bool IsDismissed { get; set; }
    public int NotificationId { get; set; }         // local notification id for cancel/update
}
```

Reminder is NOT a SyncBase subclass — it is device-local only for now. The `LocalDatabase.InitAsync()` creates the table.

## Services

### INotificationService
Thin abstraction over `Plugin.LocalNotification`, enabling unit tests without device:
```
ScheduleNotification(int id, string title, string body, DateTime fireAt, string returningData)
CancelNotification(int id)
```

### MauiNotificationService
Production implementation delegating to `LocalNotificationCenter.Current`.

### ReminderService
Orchestrates reminder CRUD + notification lifecycle:
- `ScheduleAsync(Reminder)` — inserts/updates DB, assigns NotificationId, calls INotificationService
- `SnoozeAsync(Reminder, TimeSpan duration)` — cancels old notification, sets new FireAt, reschedules
- `DismissAsync(Reminder)` — cancels notification, sets IsDismissed = true, saves to DB
- `GetPendingAsync(string accountFk)` — returns non-dismissed reminders ordered by FireAt
- `GetUpcomingForEntityAsync(string entityGuid)` — reminders attached to a specific entity

## UI

### RemindersPage (new tab/route: `reminders`)
- Lists all pending reminders grouped by Topic
- Each row: title, entity label (if any), fire time
- Row actions: Snooze, Dismiss
- FAB: "Add Reminder" → shows new reminder dialog (topic picker, optional entity navigation, date/time picker)

### Snooze Sheet
Appears as a bottom action sheet with options:
- 1 hour
- 8 hours
- 1 day (24h)
- 3 days
- Custom (text entry for number + segmented picker: Hours / Days / Weeks / Months)

### Per-entity reminder button
Added to GoalEntryPage, TodoEntryPage, JournalEntryPage:
- "🔔 Set Reminder" button → opens date/time picker → creates Reminder with EntityGuid set

### Navigation from notification tap
When user taps a notification:
- If EntityGuid is set → navigate to that entity's entry page
- Otherwise → navigate to RemindersPage

Handled in `App.xaml.cs` via `LocalNotificationCenter.Current.NotificationActionTapped`.

## Notification permission

`Plugin.LocalNotification` handles the Android 13+ `POST_NOTIFICATIONS` permission request at first use. The `UseMauiLocalNotification()` call is added to `MauiProgram.cs`.

## File Map

**New files:**
- `ChildDev.Mobile/Models/Reminder.cs`
- `ChildDev.Mobile/Data/ReminderRepository.cs`
- `ChildDev.Mobile/Services/INotificationService.cs`
- `ChildDev.Mobile/Services/MauiNotificationService.cs`
- `ChildDev.Mobile/Services/ReminderService.cs`
- `ChildDev.Mobile/ViewModels/RemindersViewModel.cs`
- `ChildDev.Mobile/Views/RemindersPage.xaml`
- `ChildDev.Mobile/Views/RemindersPage.xaml.cs`

**Modified files:**
- `ChildDev.Mobile/Models/LocalDatabase.cs` — add `CreateTableAsync<Reminder>()`
- `ChildDev.Mobile/MauiProgram.cs` — register ReminderService, ReminderRepository, INotificationService, RemindersViewModel, RemindersPage; add `.UseLocalNotification()`
- `ChildDev.Mobile/App.xaml.cs` — wire up notification tap handler
- `ChildDev.Mobile/AppShell.xaml.cs` — register `reminders` route
- `ChildDev.Mobile/ViewModels/GoalEntryViewModel.cs` — add `SetReminderCommand`
- `ChildDev.Mobile/ViewModels/TodoEntryViewModel.cs` — add `SetReminderCommand`
- `ChildDev.Mobile/ViewModels/JournalEntryViewModel.cs` — add `SetReminderCommand`
- `ChildDev.Mobile/LevelUp.csproj` — add `Plugin.LocalNotification` package reference

## Testing

Unit tests for `ReminderService` and `RemindersViewModel` using `FakeNotificationService` and in-memory SQLite, following the existing `ViewModelTestBase` pattern. No tests for the MAUI notification plugin itself (platform-only).
