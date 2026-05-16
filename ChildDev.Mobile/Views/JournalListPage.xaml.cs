using ChildDev.Mobile.ViewModels;

namespace ChildDev.Mobile.Views;

public partial class JournalListPage : ContentPage
{
    private readonly JournalListViewModel _vm;

    public JournalListPage(JournalListViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing() => _vm.LoadCommand.Execute(null);
}
