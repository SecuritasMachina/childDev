using ChildDev.Mobile.Services;

namespace ChildDev.Mobile;

public partial class App : Application
{
    public App(AccountService accountService)
    {
        InitializeComponent();

        var account = accountService.GetAccountAsync().GetAwaiter().GetResult();
        MainPage = account is null
            ? new NavigationPage(Handler?.MauiContext?.Services.GetService<Views.SetupPage>()
                ?? throw new InvalidOperationException("SetupPage not registered"))
            : new AppShell();
    }
}
