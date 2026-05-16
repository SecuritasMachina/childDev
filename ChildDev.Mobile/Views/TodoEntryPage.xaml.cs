using ChildDev.Mobile.ViewModels;

namespace ChildDev.Mobile.Views;

public partial class TodoEntryPage : ContentPage
{
    public TodoEntryPage(TodoEntryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
