using System.Net;
using System.Net.Http.Json;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

public class SettingsViewModelTests : ViewModelTestBase
{
    private SettingsViewModel BuildVm(HttpMessageHandler? handler = null)
    {
        var factory = new FakeHttpClientFactory(handler ?? new NoOpHttpHandler());
        return new SettingsViewModel(AccountService, factory, Analytics);
    }

    [Fact]
    public async Task Load_WithAccount_PopulatesProperties()
    {
        var account = await CreateTestAccountAsync("Alice", "0000");
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Alice", vm.NickName);
        Assert.Equal(account.Guid, vm.AccountGuid);
        Assert.Equal("Never", vm.LastSyncDisplay);
        Assert.False(vm.IsLinkedToServer);
    }

    [Fact]
    public async Task Load_NoAccount_DoesNotThrow()
    {
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null); // no account created
        Assert.Empty(vm.NickName);
    }

    [Fact]
    public async Task SaveServerUrl_PersistsUrl()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.ServerUrl = "https://myserver.example.com/";
        await vm.SaveServerUrlCommand.ExecuteAsync(null);

        Assert.Equal("Server URL saved.", vm.StatusMessage);
        var account = await AccountService.GetAccountAsync();
        Assert.Equal("https://myserver.example.com", account!.ServerUrl);
    }

    [Fact]
    public async Task SaveServerUrl_Empty_ClearsUrl()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.ServerUrl = string.Empty;
        await vm.SaveServerUrlCommand.ExecuteAsync(null);
        Assert.Equal("Server URL cleared.", vm.StatusMessage);
    }

    [Fact]
    public async Task TestConnection_EmptyUrl_ShowsError()
    {
        var vm = BuildVm();
        vm.ServerUrl = string.Empty;
        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.Equal("Enter a server URL first.", vm.StatusMessage);
    }

    [Fact]
    public async Task TestConnection_SuccessResponse_ShowsConnected()
    {
        var vm = BuildVm(new StatusCodeHandler(HttpStatusCode.OK));
        vm.ServerUrl = "https://test.example.com";
        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.Equal("Connected!", vm.StatusMessage);
    }

    [Fact]
    public async Task TestConnection_ServerError_ShowsErrorCode()
    {
        var vm = BuildVm(new StatusCodeHandler(HttpStatusCode.ServiceUnavailable));
        vm.ServerUrl = "https://test.example.com";
        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.Equal("Server error: 503", vm.StatusMessage);
    }

    [Fact]
    public async Task TestConnection_NetworkException_ShowsCannotReach()
    {
        var vm = BuildVm(new ThrowingHandler());
        vm.ServerUrl = "https://test.example.com";
        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.Equal("Cannot reach server.", vm.StatusMessage);
    }

    [Fact]
    public async Task LinkToServer_EmptyUrl_ShowsError()
    {
        var vm = BuildVm();
        vm.ServerUrl = string.Empty;
        vm.ServerNickName = "Alice";
        vm.ServerPin = "1234";
        await vm.LinkToServerCommand.ExecuteAsync(null);
        Assert.Equal("Save a server URL first.", vm.StatusMessage);
    }

    [Fact]
    public async Task LinkToServer_EmptyNickname_ShowsError()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.ServerUrl = "https://test.example.com";
        vm.ServerNickName = string.Empty;
        vm.ServerPin = "1234";
        await vm.LinkToServerCommand.ExecuteAsync(null);
        Assert.Equal("Enter your server account nickname.", vm.StatusMessage);
    }

    [Fact]
    public async Task LinkToServer_EmptyPin_ShowsError()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.ServerUrl = "https://test.example.com";
        vm.ServerNickName = "Alice";
        vm.ServerPin = string.Empty;
        await vm.LinkToServerCommand.ExecuteAsync(null);
        Assert.Equal("Enter your server account password.", vm.StatusMessage);
    }

    [Fact]
    public async Task LinkToServer_Unauthorized_ShowsIncorrectMessage()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm(new StatusCodeHandler(HttpStatusCode.Unauthorized));
        vm.ServerUrl = "https://test.example.com";
        vm.ServerNickName = "Alice";
        vm.ServerPin = "1234";
        await vm.LinkToServerCommand.ExecuteAsync(null);
        Assert.Equal("Incorrect nickname or password.", vm.StatusMessage);
        Assert.False(vm.IsLinking);
    }

    [Fact]
    public async Task LinkToServer_NetworkException_ShowsCannotConnect()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm(new ThrowingHandler());
        vm.ServerUrl = "https://test.example.com";
        vm.ServerNickName = "Alice";
        vm.ServerPin = "1234";
        await vm.LinkToServerCommand.ExecuteAsync(null);
        Assert.Equal("Could not connect to server.", vm.StatusMessage);
        Assert.False(vm.IsLinking);
    }

    [Fact]
    public async Task LinkToServer_Success_SetsLinkedAndClearsCredentials()
    {
        var account = await CreateTestAccountAsync();
        var jwt = "test.jwt.token";
        var authJson = $"{{\"Jwt\":\"{jwt}\",\"AccountGuid\":\"{account.Guid}\"}}";
        var vm = BuildVm(new JsonResponseHandler(authJson));
        vm.ServerUrl = "https://test.example.com";
        vm.ServerNickName = "Alice";
        vm.ServerPin = "1234";
        await vm.LinkToServerCommand.ExecuteAsync(null);

        Assert.True(vm.IsLinkedToServer);
        Assert.Empty(vm.ServerNickName);
        Assert.Empty(vm.ServerPin);
        Assert.Equal("Linked to server!", vm.StatusMessage);
    }

    [Fact]
    public async Task UnlinkFromServer_ClearsLinkedState()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.ServerUrl = "https://test.example.com";
        await vm.UnlinkFromServerCommand.ExecuteAsync(null);

        Assert.False(vm.IsLinkedToServer);
        Assert.Equal("Unlinked from server.", vm.StatusMessage);
    }

    [Fact]
    public void BuildDisplay_ContainsValidText()
    {
        var vm = BuildVm();
        Assert.StartsWith("Build: ", vm.BuildDisplay);
    }
}

public class StatusCodeHandler(HttpStatusCode statusCode) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(new HttpResponseMessage(statusCode));
}

public class ThrowingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => throw new HttpRequestException("Network error");
}

public class JsonResponseHandler(string json) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
}
