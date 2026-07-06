#if ANDROID || IOS || MACCATALYST || WINDOWS
using Plugin.LocalNotification;
#endif
using LevelUp.Data;
using LevelUp.Services;

namespace LevelUp;

public partial class App : Application
{
    public App(LocalDatabase localDb, IServiceProvider services)
    {
        InitializeComponent();
        MainPage = BuildSplashPage();

        // Bring up the encrypted local DB FULLY ASYNCHRONOUSLY, off the UI thread. The per-device key
        // lives in Android SecureStorage (Keystore/Tink-backed); fetching it, migrating a legacy
        // plaintext DB, and opening the SQLCipher connection all happen inside InitAsync(). Touching any
        // of this synchronously on the UI thread deadlocks on the Keystore and hangs the app on the
        // splash screen (Google Play rejection, .NET 9). Nothing DB-backed is resolved until the
        // connection is open, because those constructors capture the SQLiteAsyncConnection.
        Task.Run(async () =>
        {
            try
            {
                await localDb.InitAsync();

                var bizEyes = services.GetService<BizEyesAnalyticsService>();
                WireExceptionForwarding(bizEyes);

                var accountService = services.GetRequiredService<AccountService>();
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
            }
            catch (Exception ex)
            {
                // Fail safe: never leave the app hanging on the splash. Surface the failure so it is
                // visible (and reviewable) rather than an infinite blank load.
                MainThread.BeginInvokeOnMainThread(() => MainPage = BuildStartupErrorPage(ex));
            }
        });

#if ANDROID || IOS || MACCATALYST || WINDOWS
        LocalNotificationCenter.Current.NotificationActionTapped += OnNotificationTapped;
#endif
    }

    // Forward unhandled exceptions to AnalyticsHub (bizeyes). Wired once the analytics service is
    // available (after the DB is up) since it depends on DB-backed account state.
    private static void WireExceptionForwarding(BizEyesAnalyticsService? bizEyes)
    {
        if (bizEyes is null) return;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) bizEyes.TrackException(ex, isHandled: false);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
            bizEyes.TrackException(e.Exception, isHandled: false);
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

    // Shown if startup DB initialisation fails outright (fail-safe; must never hang on the splash).
    private static ContentPage BuildStartupErrorPage(Exception ex) => new()
    {
        BackgroundColor = Color.FromArgb("#512BD4"),
        Padding = new Thickness(24),
        Content = new VerticalStackLayout
        {
            Spacing = 12,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = "LevelUp couldn't start",
                    TextColor = Colors.White,
                    FontSize = 22,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = "Please reopen the app. If this keeps happening, reinstall to reset local data.",
                    TextColor = Color.FromArgb("#E6FFFFFF"),
                    FontSize = 14,
                    HorizontalTextAlignment = TextAlignment.Center
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
