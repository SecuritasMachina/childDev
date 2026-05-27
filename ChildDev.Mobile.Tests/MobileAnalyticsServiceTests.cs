using LevelUp.Services;

namespace LevelUp.Tests;

public class MobileAnalyticsServiceTests : ViewModelTestBase
{
    [Fact]
    public async Task Track_NoAccount_DoesNotThrow()
    {
        // No account created — service should silently skip
        var svc = new MobileAnalyticsService(AccountService, new FakeHttpClientFactory(new NoOpHttpHandler()));
        svc.Track("test_event");
        await Task.Delay(100); // allow fire-and-forget to settle
        // no exception = pass
    }

    [Fact]
    public async Task Track_AccountNoServerUrl_DoesNotSendRequest()
    {
        await CreateTestAccountAsync();
        var handler = new NotCalledHandler();
        var svc = new MobileAnalyticsService(AccountService, new FakeHttpClientFactory(handler));
        svc.Track("test_event");
        await Task.Delay(100);
        // NotCalledHandler throws if called; reaching here means it was not called
    }

    [Fact]
    public async Task Track_NetworkError_DoesNotCrash()
    {
        var account = await CreateTestAccountAsync();
        await AccountService.SaveServerUrlAsync("https://test.example.com");
        await AccountService.LinkToServerAsync("fake.jwt", "https://test.example.com", account.Guid);

        var svc = new MobileAnalyticsService(AccountService, new FakeHttpClientFactory(new ThrowingHandler()));
        svc.Track("test_event");
        await Task.Delay(200); // let fire-and-forget resolve
        // no crash = pass
    }

    [Fact]
    public async Task Track_WithServerConnection_SendsRequest()
    {
        var account = await CreateTestAccountAsync();
        await AccountService.SaveServerUrlAsync("https://test.example.com");
        await AccountService.LinkToServerAsync("fake.jwt", "https://test.example.com", account.Guid);

        var recordingHandler = new RecordingHttpHandler();
        var svc = new MobileAnalyticsService(AccountService, new FakeHttpClientFactory(recordingHandler));
        svc.Track("goal_create", "ctx");
        await Task.Delay(300);

        Assert.Single(recordingHandler.Requests);
        Assert.Contains("events", recordingHandler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task Track_NullContext_DoesNotThrow()
    {
        await CreateTestAccountAsync();
        var svc = new MobileAnalyticsService(AccountService, new FakeHttpClientFactory(new NoOpHttpHandler()));
        svc.Track("page_view", null);
        await Task.Delay(100);
    }
}

public class RecordingHttpHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
