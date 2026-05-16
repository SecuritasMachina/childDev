using ChildDev.Mobile.ViewModels;

namespace ChildDev.Mobile.Views;

public partial class GoalListPage : ContentPage
{
    public GoalListPage(GoalListViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
