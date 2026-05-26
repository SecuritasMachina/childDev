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
