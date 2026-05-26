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
