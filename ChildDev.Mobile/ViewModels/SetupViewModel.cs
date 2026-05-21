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
    [NotifyCanExecuteChangedFor(nameof(CreateAccountCommand))]
    private string confirmPin = string.Empty;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    private bool CanCreate => !string.IsNullOrWhiteSpace(NickName)
        && Pin.Length == 4 && Pin.All(char.IsDigit)
        && ConfirmPin == Pin;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAccountAsync()
    {
        if (!Pin.All(char.IsDigit))
        {
            ErrorMessage = "PIN must be 4 digits";
            return;
        }
        if (Pin != ConfirmPin)
        {
            ErrorMessage = "PINs do not match";
            return;
        }

        try
        {
            await accountService.CreateAccountAsync(NickName.Trim(), Pin);
            Application.Current!.MainPage = new AppShell();
        }
        catch
        {
            ErrorMessage = "Could not create account. Please try again.";
        }
    }
}
