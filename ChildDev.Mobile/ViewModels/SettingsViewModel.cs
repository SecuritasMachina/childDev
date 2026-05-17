using ChildDev.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildDev.Mobile.ViewModels;

public partial class SettingsViewModel(AccountService accountService, IHttpClientFactory httpFactory) : ObservableObject
{
    [ObservableProperty] private string serverUrl = string.Empty;
    [ObservableProperty] private string nickName = string.Empty;
    [ObservableProperty] private string accountCreatedDisplay = string.Empty;
    [ObservableProperty] private string lastSyncDisplay = "Never";
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string accountGuid = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;
        NickName = account.NickName;
        AccountGuid = account.Guid;
        ServerUrl = account.ServerUrl ?? string.Empty;
        AccountCreatedDisplay = DateTimeOffset.FromUnixTimeMilliseconds(account.CreatedOn).LocalDateTime.ToString("ddd, MMM d yyyy");
        LastSyncDisplay = account.LastSyncAt == 0
            ? "Never"
            : DateTimeOffset.FromUnixTimeMilliseconds(account.LastSyncAt).LocalDateTime.ToString("g");
    }

    [RelayCommand]
    private async Task SaveServerUrlAsync()
    {
        var url = ServerUrl.Trim().TrimEnd('/');
        await accountService.SaveServerUrlAsync(url);
        StatusMessage = "Server URL saved.";
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        var url = ServerUrl.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(url)) { StatusMessage = "Enter a server URL first."; return; }
        StatusMessage = "Testing...";
        try
        {
            var client = httpFactory.CreateClient("childdev");
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = await client.GetAsync($"{url}/health");
            StatusMessage = response.IsSuccessStatusCode ? "Connected!" : $"Server error: {(int)response.StatusCode}";
        }
        catch
        {
            StatusMessage = "Cannot reach server.";
        }
    }
}
