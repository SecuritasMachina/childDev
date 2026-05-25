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
    }
}
