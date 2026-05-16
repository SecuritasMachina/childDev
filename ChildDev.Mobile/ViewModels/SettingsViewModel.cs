using ChildDev.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildDev.Mobile.ViewModels;

public partial class SettingsViewModel(AccountService accountService) : ObservableObject
{
    [ObservableProperty] private string serverUrl = string.Empty;
    [ObservableProperty] private string nickName = string.Empty;
    [ObservableProperty] private string lastSyncDisplay = "Never";
    [ObservableProperty] private string statusMessage = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;
        NickName = account.NickName;
        ServerUrl = account.ServerUrl ?? string.Empty;
        LastSyncDisplay = account.LastSyncAt == 0
            ? "Never"
            : DateTimeOffset.FromUnixTimeMilliseconds(account.LastSyncAt).LocalDateTime.ToString("g");
    }

    [RelayCommand]
    private async Task SaveServerUrlAsync()
    {
        var url = ServerUrl.Trim().TrimEnd('/');
        await accountService.SaveServerCredentialsAsync(string.Empty, url);
        StatusMessage = "Server URL saved.";
    }
}
