using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelUp.Services;

namespace LevelUp.ViewModels;

public partial class SetupViewModel(AccountService accountService) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateAccountCommand))]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private string nickName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateAccountCommand))]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private string pin = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateAccountCommand))]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private string confirmPin = string.Empty;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CreateButtonText))]
    [NotifyCanExecuteChangedFor(nameof(CreateAccountCommand))]
    private bool isCreating;

    public string CreateButtonText => IsCreating ? "Setting up account..." : "Get Started";

    private bool CanCreate => !string.IsNullOrWhiteSpace(NickName)
        && Pin.Length >= 4
        && ConfirmPin == Pin
        && !IsCreating;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAccountAsync()
    {
        if (Pin != ConfirmPin)
        {
            ErrorMessage = "Passwords do not match";
            return;
        }

        IsCreating = true;
        ErrorMessage = string.Empty;
        try
        {
            await accountService.CreateAccountAsync(NickName.Trim(), Pin);
            Application.Current!.MainPage = new AppShell();
        }
        catch
        {
            ErrorMessage = "Could not create account. Please try again.";
            IsCreating = false;
        }
    }
}
