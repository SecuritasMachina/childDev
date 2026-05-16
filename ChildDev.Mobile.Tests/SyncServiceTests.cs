using System.Net;
using System.Net.Http.Json;
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using SQLite;

namespace ChildDev.Mobile.Tests;

public class SyncServiceTests : IDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private readonly JournalRepository _journalRepo;
    private readonly GoalRepository _goalRepo;
    private readonly GoalProgressRepository _goalProgressRepo;
    private readonly TodoRepository _todoRepo;
    private readonly AccountService _accountService;

    public SyncServiceTests()
    {
        SqliteFixture.EnsureInit();
        _db = new SQLiteAsyncConnection(":memory:");
        _db.CreateTableAsync<Account>().GetAwaiter().GetResult();
        _db.CreateTableAsync<Journal>().GetAwaiter().GetResult();
        _db.CreateTableAsync<Goal>().GetAwaiter().GetResult();
        _db.CreateTableAsync<GoalProgress>().GetAwaiter().GetResult();
        _db.CreateTableAsync<Todo>().GetAwaiter().GetResult();

        _journalRepo = new JournalRepository(_db);
        _goalRepo = new GoalRepository(_db);
        _goalProgressRepo = new GoalProgressRepository(_db);
        _todoRepo = new TodoRepository(_db);
        _accountService = new AccountService(_db);
    }

    public void Dispose() => _db.CloseAsync().GetAwaiter().GetResult();

    private SyncService BuildSyncService(HttpMessageHandler handler, bool isConnected = true)
    {
        var connectivity = new FakeConnectivityService(isConnected);
        var factory = new FakeHttpClientFactory(handler);
        return new SyncService(_journalRepo, _goalRepo, _goalProgressRepo, _todoRepo,
            _accountService, connectivity, factory);
    }

    [Fact]
    public async Task RunAsync_NoServer_ReturnsNoServer()
    {
        await _accountService.CreateAccountAsync("user1", "1234");
        var account = await _accountService.GetAccountAsync();

        var service = BuildSyncService(new NotCalledHandler());
        var result = await service.RunAsync(account!);

        Assert.Equal(SyncResult.NoServer, result);
    }

    [Fact]
    public async Task RunAsync_NotConnected_ReturnsNoServer()
    {
        await _accountService.CreateAccountAsync("user2", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var service = BuildSyncService(new NotCalledHandler(), isConnected: false);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.NoServer, result);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsData_UpsertsLocally()
    {
        await _accountService.CreateAccountAsync("user3", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var serverJournal = new JournalSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, "From server",
            null, null, null, 1000, 1000, null);

        var handler = new FakeSyncHandler(serverJournal);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var journals = await _journalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(journals);
        Assert.Equal("From server", journals[0].Notes);
    }

    [Fact]
    public async Task RunAsync_ServerError_ReturnsFailed()
    {
        await _accountService.CreateAccountAsync("user4", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var service = BuildSyncService(new ErrorHandler());
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Failed, result);
    }
}

// Test helpers
public class FakeConnectivityService(bool isConnected) : ConnectivityService
{
    public override bool IsConnected => isConnected;
}

public class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler);
}

public class NotCalledHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Should not have been called");
}

public class ErrorHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
}

public class FakeSyncHandler(JournalSyncDto journal) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri!.PathAndQuery.Contains("journal"))
        {
            var response = new SyncResponseDto<JournalSyncDto>([journal]);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(response)
            };
        }
        // Return empty for other entity types
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { Records = Array.Empty<object>() })
        };
    }
}
