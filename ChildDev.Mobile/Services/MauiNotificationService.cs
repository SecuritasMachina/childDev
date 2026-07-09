namespace LevelUp.Services;

public class MauiNotificationService : INotificationService
{
    // Notifications are best-effort: a permission denial or platform quirk must NEVER crash the
    // caller (a reminder is still persisted even if its notification can't be posted). MAUI's
    // permission check itself THROWS if POST_NOTIFICATIONS isn't declared in the manifest (Android
    // 13+), which previously hard-crashed reminder scheduling — so these all fail safe.
#if ANDROID || IOS || MACCATALYST || WINDOWS
    public async Task<bool> RequestPermissionAsync()
    {
        try
        {
            return await Plugin.LocalNotification.LocalNotificationCenter.Current.RequestNotificationPermission();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Notifications] permission request failed: {ex.Message}");
            return false;
        }
    }

    public async Task ScheduleAsync(int id, string title, string body, DateTime fireAt, string returningData)
    {
        try
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Notifications] schedule failed: {ex.Message}");
        }
    }

    public Task CancelAsync(int id)
    {
        try
        {
            Plugin.LocalNotification.LocalNotificationCenter.Current.Cancel(id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Notifications] cancel failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }
#else
    public Task<bool> RequestPermissionAsync() => Task.FromResult(true);
    public Task ScheduleAsync(int id, string title, string body, DateTime fireAt, string returningData) => Task.CompletedTask;
    public Task CancelAsync(int id) => Task.CompletedTask;
#endif
}
