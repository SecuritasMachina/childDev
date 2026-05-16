using ChildDev.Mobile.ViewModels;

namespace ChildDev.Mobile.Views;

public partial class GoalListPage : ContentPage
{
    private readonly GoalListViewModel _vm;

    public GoalListPage(GoalListViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing() => _vm.LoadCommand.Execute(null);
}
