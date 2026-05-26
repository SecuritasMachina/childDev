using LevelUp.ViewModels;
using LevelUp.Models;

namespace LevelUp.Views;

public partial class RemindersPage : ContentPage
{
    public RemindersPage(RemindersViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is RemindersViewModel vm)
            vm.LoadCommand.Execute(null);
    }

    private async void OnSnoozeClicked(object sender, EventArgs e)
    {
        if (sender is SwipeItem item && item.BindingContext is Reminder r
            && BindingContext is RemindersViewModel vm)
            await vm.SnoozeCommand.ExecuteAsync(r);
    }

    private async void OnDismissClicked(object sender, EventArgs e)
    {
        if (sender is SwipeItem item && item.BindingContext is Reminder r
            && BindingContext is RemindersViewModel vm)
            await vm.DismissCommand.ExecuteAsync(r);
    }
}
