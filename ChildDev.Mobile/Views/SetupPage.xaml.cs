using ChildDev.Mobile.ViewModels;

namespace ChildDev.Mobile.Views;

public partial class SetupPage : ContentPage
{
    public SetupPage(SetupViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
