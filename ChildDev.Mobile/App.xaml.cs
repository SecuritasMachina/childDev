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
        MainPage = BuildSplashPage();

        var bizEyes = services.GetService<Services.BizEyesAnalyticsService>();

        Task.Run(async () =>
        {
            await localDb.InitAsync();
            var account = await accountService.GetAccountAsync();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (account is null)
                {
                    MainPage = new NavigationPage(services.GetRequiredService<Views.SetupPage>());
                }
                else
                {
                    var shell = new AppShell();
                    // Forward screen views to AnalyticsHub (bizeyes) as page views.
                    if (bizEyes is not null)
                        shell.Navigated += (_, e) =>
                            bizEyes.TrackScreenView(e.Current?.Location?.OriginalString ?? "/");
                    MainPage = shell;
                }
            });
        });

        // Forward unhandled exceptions to AnalyticsHub (bizeyes).
        if (bizEyes is not null)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex) bizEyes.TrackException(ex, isHandled: false);
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
                bizEyes.TrackException(e.Exception, isHandled: false);
        }
#if ANDROID || IOS || MACCATALYST || WINDOWS
        LocalNotificationCenter.Current.NotificationActionTapped += OnNotificationTapped;
#endif
    }

    // Interim splash shown while the local DB initialises. Carries the build
    // timestamp so the running build is identifiable at launch.
    private static ContentPage BuildSplashPage() => new()
    {
        BackgroundColor = Color.FromArgb("#512BD4"),
        Content = new Grid
        {
            Children =
            {
                new Label
                {
                    Text = "LevelUp",
                    TextColor = Colors.White,
                    FontSize = 32,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = $"Build: {BuildInfo.BuildTimestamp}",
                    TextColor = Color.FromArgb("#99FFFFFF"),
                    FontSize = 11,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.End,
                    Margin = new Thickness(0, 0, 0, 24)
                }
            }
        }
    };

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
