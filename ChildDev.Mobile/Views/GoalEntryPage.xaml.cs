using ChildDev.Mobile.ViewModels;

namespace ChildDev.Mobile.Views;

public partial class GoalEntryPage : ContentPage
{
    public GoalEntryPage(GoalEntryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
