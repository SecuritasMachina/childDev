using LevelUp.ViewModels;

namespace LevelUp.Views;

public partial class TodoListPage : ContentPage
{
    private readonly TodoListViewModel _vm;

    public TodoListPage(TodoListViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing() => _vm.LoadCommand.Execute(null);
}
