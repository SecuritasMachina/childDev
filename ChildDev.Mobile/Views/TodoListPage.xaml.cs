using ChildDev.Mobile.ViewModels;

namespace ChildDev.Mobile.Views;

public partial class TodoListPage : ContentPage
{
    public TodoListPage(TodoListViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
