namespace LevelUp.Services;

public interface INotificationService
{
    Task<bool> RequestPermissionAsync();
    Task ScheduleAsync(int id, string title, string body, DateTime fireAt, string returningData);
    Task CancelAsync(int id);
}
