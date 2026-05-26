using LevelUp.Data;
using LevelUp.Services;
using LevelUp.ViewModels;
using LevelUp.Views;
using SQLite;

namespace LevelUp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "childdev.db3");

        var localDb = new LocalDatabase(dbPath);
        builder.Services.AddSingleton(localDb);
        builder.Services.AddSingleton(localDb.Connection);
        builder.Services.AddSingleton<AccountService>();
        builder.Services.AddSingleton<JournalRepository>();
        builder.Services.AddSingleton<GoalRepository>();
        builder.Services.AddSingleton<GoalProgressRepository>();
        builder.Services.AddSingleton<TodoRepository>();
        builder.Services.AddSingleton<ConnectivityService>();
        builder.Services.AddSingleton<SyncService>();
        builder.Services.AddSingleton<MobileAnalyticsService>();
        builder.Services.AddSingleton<INavigationService, MauiNavigationService>();
        builder.Services.AddHttpClient("childdev");

        // ViewModels (transient -- new instance per navigation)
        builder.Services.AddTransient<SetupViewModel>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<JournalListViewModel>();
        builder.Services.AddTransient<JournalEntryViewModel>();
        builder.Services.AddTransient<GoalListViewModel>();
        builder.Services.AddTransient<GoalEntryViewModel>();
        builder.Services.AddTransient<TodoListViewModel>();
        builder.Services.AddTransient<TodoEntryViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

        // Pages
        builder.Services.AddTransient<SetupPage>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<JournalListPage>();
        builder.Services.AddTransient<JournalEntryPage>();
        builder.Services.AddTransient<GoalListPage>();
        builder.Services.AddTransient<GoalEntryPage>();
        builder.Services.AddTransient<TodoListPage>();
        builder.Services.AddTransient<TodoEntryPage>();
        builder.Services.AddTransient<SettingsPage>();

        return builder.Build();
    }
}
