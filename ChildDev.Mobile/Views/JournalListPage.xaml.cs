using ChildDev.Mobile.ViewModels;

namespace ChildDev.Mobile.Views;

public partial class JournalListPage : ContentPage
{
    public JournalListPage(JournalListViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
