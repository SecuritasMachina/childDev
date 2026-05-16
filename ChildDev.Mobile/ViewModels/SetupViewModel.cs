using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildDev.Mobile.Services;

namespace ChildDev.Mobile.ViewModels;

public partial class SetupViewModel(AccountService accountService) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateAccountCommand))]
    private string nickName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateAccountCommand))]
    private string pin = string.Empty;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    private bool CanCreate => !string.IsNullOrWhiteSpace(NickName) && Pin.Length == 4;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAccountAsync()
    {
        if (!Pin.All(char.IsDigit))
        {
            ErrorMessage = "PIN must be 4 digits";
            return;
        }

        await accountService.CreateAccountAsync(NickName.Trim(), Pin);
        await Shell.Current.GoToAsync("//dashboard");
    }
}
