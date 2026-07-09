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

        // Install a global crash-capture net FIRST — before any startup work runs — so an unhandled
        // exception anywhere (a background init task, the UI thread, or first-page construction) is
        // logged with a full stack (logcat + an on-device file) instead of vanishing into a bare
        // process abort. This makes a "crashes after opening" report self-diagnosing on the next run.
        InstallGlobalCrashCapture();

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
                    // Building the first real page (SetupPage / AppShell) runs on the UI thread OUTSIDE
                    // the outer try/catch, so an exception here would be an UNHANDLED hard crash right
                    // after the splash ("crashes after opening"). Guard it and fall back to the error
                    // page instead of letting the process die.
                    try
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
                    }
                    catch (Exception ex)
                    {
                        CaptureCrash("startup-navigation", ex);
                        bizEyes?.TrackException(ex, isHandled: true);
                        MainPage = BuildStartupErrorPage(ex);
                    }
                });
            }
            catch (Exception ex)
            {
                // Fail safe: never leave the app hanging on the splash. Surface the failure so it is
                // visible (and reviewable) rather than an infinite blank load.
                CaptureCrash("startup-init", ex);
                MainThread.BeginInvokeOnMainThread(() => MainPage = BuildStartupErrorPage(ex));
            }
        });

#if ANDROID || IOS || MACCATALYST || WINDOWS
        LocalNotificationCenter.Current.NotificationActionTapped += OnNotificationTapped;
#endif
    }

    // Process-wide last-resort capture. Independent of any DB/analytics state so it works from the very
    // first instruction of startup. It only LOGS (does not swallow) — swallowing a native/runtime
    // unhandled exception can leave the process in an undefined state; the specific recoverable window
    // (first-page construction) is handled inline above with a real error-page fallback.
    private static void InstallGlobalCrashCapture()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) CaptureCrash("appdomain-unhandled", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CaptureCrash("unobserved-task", e.Exception);
            e.SetObserved();
        };
#if ANDROID
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
            CaptureCrash("android-unhandled", e.Exception);
#endif
    }

    // Best-effort: write the full exception (with stack) to logcat AND an on-device log file so a crash
    // can be pulled with `adb`/`run-as` after the fact. Never throws.
    private static void CaptureCrash(string source, Exception? ex)
    {
        var message = $"[LevelUp CRASH:{source}] {ex}";
        try { System.Diagnostics.Debug.WriteLine(message); } catch { /* ignore */ }
#if ANDROID
        try { Android.Util.Log.Error("LevelUpCrash", message); } catch { /* ignore */ }
#endif
        try
        {
            var path = Path.Combine(FileSystem.AppDataDirectory, "startup-crash.log");
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:o} {message}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* best-effort — never let logging crash the crash handler */ }
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
