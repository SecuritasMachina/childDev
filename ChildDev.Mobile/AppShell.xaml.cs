namespace ChildDev.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("journal/entry", typeof(Views.JournalEntryPage));
        Routing.RegisterRoute("goals/entry", typeof(Views.GoalEntryPage));
        Routing.RegisterRoute("settings", typeof(Views.SettingsPage));
    }
}
