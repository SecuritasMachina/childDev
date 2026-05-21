using ChildDev.Mobile.Services;

namespace ChildDev.Mobile;

public partial class App : Application
{
    public App(AccountService accountService, IServiceProvider services)
    {
        InitializeComponent();

        var account = accountService.GetAccountAsync().GetAwaiter().GetResult();
        MainPage = account is null
            ? new NavigationPage(services.GetRequiredService<Views.SetupPage>())
            : new AppShell();
    }
}
