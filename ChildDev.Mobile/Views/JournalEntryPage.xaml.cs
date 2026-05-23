using LevelUp.ViewModels;

namespace LevelUp.Views;

public partial class JournalEntryPage : ContentPage
{
    public JournalEntryPage(JournalEntryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
