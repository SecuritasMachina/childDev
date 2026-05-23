using LevelUp.ViewModels;

namespace LevelUp.Views;

public partial class TodoEntryPage : ContentPage
{
    public TodoEntryPage(TodoEntryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
