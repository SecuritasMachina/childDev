using System.Net.Http.Json;
using LevelUp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LevelUp.ViewModels;

public partial class SettingsViewModel(AccountService accountService, IHttpClientFactory httpFactory, MobileAnalyticsService analytics) : ObservableObject
{
    [ObservableProperty] private string serverUrl = string.Empty;
    [ObservableProperty] private string nickName = string.Empty;
    [ObservableProperty] private string accountCreatedDisplay = string.Empty;
    [ObservableProperty] private string lastSyncDisplay = "Never";
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string accountGuid = string.Empty;
    [ObservableProperty] private bool isLinkedToServer;
    public string BuildDisplay { get; } = $"Build: {BuildInfo.BuildTimestamp}";

    // Server link fields
    [ObservableProperty] private string serverNickName = string.Empty;
    [ObservableProperty] private string serverPin = string.Empty;
    [ObservableProperty] private bool isLinking;

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var account = await accountService.GetAccountAsync();
            if (account is null) return;
            analytics.Track("settings_view");
            NickName = account.NickName;
            AccountGuid = account.Guid;
            ServerUrl = account.ServerUrl ?? string.Empty;
            IsLinkedToServer = !string.IsNullOrEmpty(account.ServerJwt);
            AccountCreatedDisplay = DateTimeOffset.FromUnixTimeMilliseconds(account.CreatedOn).LocalDateTime.ToString("ddd, MMM d yyyy");
            LastSyncDisplay = account.LastSyncAt == 0
                ? "Never"
                : DateTimeOffset.FromUnixTimeMilliseconds(account.LastSyncAt).LocalDateTime.ToString("g");
        }
        catch
        {
            StatusMessage = "Could not load settings.";
        }
    }

    [RelayCommand]
    private async Task SaveServerUrlAsync()
    {
        var url = ServerUrl.Trim().TrimEnd('/');
        await accountService.SaveServerUrlAsync(url);
        StatusMessage = string.IsNullOrEmpty(url) ? "Server URL cleared." : "Server URL saved.";
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

    [RelayCommand]
    private async Task LinkToServerAsync()
    {
        var url = ServerUrl.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(url)) { StatusMessage = "Save a server URL first."; return; }
        if (string.IsNullOrWhiteSpace(ServerNickName)) { StatusMessage = "Enter your server account nickname."; return; }
        if (string.IsNullOrWhiteSpace(ServerPin)) { StatusMessage = "Enter your server account password."; return; }

        IsLinking = true;
        StatusMessage = "Linking...";
        try
        {
            var client = httpFactory.CreateClient("childdev");
            client.Timeout = TimeSpan.FromSeconds(10);
            var response = await client.PostAsJsonAsync($"{url}/api/auth/token",
                new { NickName = ServerNickName.Trim(), PinHash = ServerPin });

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                StatusMessage = "Incorrect nickname or password.";
                return;
            }
            response.EnsureSuccessStatusCode();

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (auth is null) { StatusMessage = "Invalid server response."; return; }

            await accountService.LinkToServerAsync(auth.Jwt, url, auth.AccountGuid);
            IsLinkedToServer = true;
            ServerNickName = string.Empty;
            ServerPin = string.Empty;
            StatusMessage = "Linked to server!";
            await LoadAsync();
        }
        catch
        {
            StatusMessage = "Could not connect to server.";
        }
        finally
        {
            IsLinking = false;
        }
    }

    [RelayCommand]
    private async Task UnlinkFromServerAsync()
    {
        await accountService.ClearServerJwtAsync();
        IsLinkedToServer = false;
        StatusMessage = "Unlinked from server.";
    }

    private record AuthResponse(string Jwt, string AccountGuid);
}
