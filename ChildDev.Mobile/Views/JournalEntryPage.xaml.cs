using ChildDev.Mobile.ViewModels;

namespace ChildDev.Mobile.Views;

public partial class JournalEntryPage : ContentPage
{
    public JournalEntryPage(JournalEntryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
