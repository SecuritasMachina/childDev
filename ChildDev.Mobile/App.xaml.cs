using LevelUp.Services;

namespace LevelUp;

public partial class App : Application
{
    public App(AccountService accountService, IServiceProvider services)
    {
        InitializeComponent();

        var account = Task.Run(accountService.GetAccountAsync).GetAwaiter().GetResult();
        MainPage = account is null
            ? new NavigationPage(services.GetRequiredService<Views.SetupPage>())
            : new AppShell();
    }
}
