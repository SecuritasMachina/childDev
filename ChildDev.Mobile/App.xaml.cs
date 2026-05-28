#if ANDROID || IOS || MACCATALYST || WINDOWS
using Plugin.LocalNotification;
#endif
using LevelUp.Data;
using LevelUp.Services;

namespace LevelUp;

public partial class App : Application
{
    public App(LocalDatabase localDb, AccountService accountService, IServiceProvider services)
    {
        InitializeComponent();
        MainPage = new ContentPage { BackgroundColor = Color.FromArgb("#512BD4") };

        Task.Run(async () =>
        {
            await localDb.InitAsync();
            var account = await accountService.GetAccountAsync();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                MainPage = account is null
                    ? new NavigationPage(services.GetRequiredService<Views.SetupPage>())
                    : new AppShell();
            });
        });
#if ANDROID || IOS || MACCATALYST || WINDOWS
        LocalNotificationCenter.Current.NotificationActionTapped += OnNotificationTapped;
#endif
    }

#if ANDROID || IOS || MACCATALYST || WINDOWS
    private void OnNotificationTapped(Plugin.LocalNotification.EventArgs.NotificationActionEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Shell.Current is not null)
                await Shell.Current.GoToAsync("///reminders");
        });
    }
#endif
}
