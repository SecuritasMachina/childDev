using ChildDev.Mobile.ViewModels;

namespace ChildDev.Mobile.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _vm;

    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing() => _vm.LoadCommand.Execute(null);
}
