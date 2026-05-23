using LevelUp.ViewModels;

namespace LevelUp.Views;

public partial class GoalEntryPage : ContentPage
{
    public GoalEntryPage(GoalEntryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
