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
    public async Task RunAsync_HealthFails_ReturnsNoServer()
    {
        await _accountService.CreateAccountAsync("user4", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        // Health returns 500 → server unreachable → NoServer (not Failed)
        var service = BuildSyncService(new ErrorHandler());
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.NoServer, result);
    }

    [Fact]
    public async Task RunAsync_EntitySyncFails_ReturnsFailed()
    {
        await _accountService.CreateAccountAsync("user4b", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        // Health passes but entity syncs fail → Failed
        var service = BuildSyncService(new EntitySyncErrorHandler());
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Failed, result);
    }

    [Fact]
    public async Task RunAsync_Success_UpdatesLastSyncAtToSyncStartTime()
    {
        await _accountService.CreateAccountAsync("user5", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var serverJournal = new JournalSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, "Sync test",
            null, null, null, 1000, 1000, null);

        var beforeSync = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var handler = new FakeSyncHandler(serverJournal);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);
        var afterSync = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Assert.Equal(SyncResult.Success, result);
        var refreshed = await _accountService.GetAccountAsync();
        // LastSyncAt must be the timestamp captured at sync START, within the call window
        Assert.True(refreshed!.LastSyncAt >= beforeSync, "LastSyncAt should be >= time before sync started");
        Assert.True(refreshed.LastSyncAt <= afterSync, "LastSyncAt should be <= time after sync completed (captured at start, not end)");
    }

    [Fact]
    public async Task RunAsync_EntitySyncTransient5xx_RetriesAndSucceeds()
    {
        await _accountService.CreateAccountAsync("user6b", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        // First entity call returns 500, second returns 200 — simulates transient failure
        var service = BuildSyncService(new TransientFailThenSucceedHandler());
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
    }

    [Fact]
    public async Task RunAsync_ConcurrentCall_SkipsSecondSync()
    {
        await _accountService.CreateAccountAsync("user7", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var serverJournal = new JournalSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, "Concurrent test",
            null, null, null, 1000, 1000, null);

        var handler = new FakeSyncHandler(serverJournal);
        var service = BuildSyncService(handler);

        // First sync sets _syncing=1 synchronously before the first await, then suspends at health call.
        var firstSync = service.RunAsync(account);

        // Second call while first is in-flight: _syncing guard returns Success immediately.
        var secondResult = await service.RunAsync(account);

        var firstResult = await firstSync;

        Assert.Equal(SyncResult.Success, firstResult);
        Assert.Equal(SyncResult.Success, secondResult);
    }

    [Fact]
    public async Task RunAsync_LocalJournalModifiedSinceLastSync_IncludedInRequest()
    {
        await _accountService.CreateAccountAsync("user8", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        // Insert a journal directly (bypassing SaveAsync so UpdatedOn stays fixed)
        var journalGuid = System.Guid.NewGuid().ToString();
        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new Journal
        {
            Guid = journalGuid,
            AccountFk = account.Guid,
            Notes = "local note",
            EnteredDate = updatedOn,
            UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var journalBody = capturingHandler.GetBodyFor("sync/journal");
        Assert.NotNull(journalBody);
        Assert.Contains(journalGuid, journalBody);
    }

    [Fact]
    public async Task RunAsync_PartialFailure_DoesNotUpdateLastSyncAt()
    {
        await _accountService.CreateAccountAsync("user6", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";
        var originalLastSync = account.LastSyncAt; // 0 (never synced)

        // Handler: health + journal succeed, goal returns 500 → exception → no LastSyncAt update
        var service = BuildSyncService(new GoalFailureHandler());
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Failed, result);
        var refreshed = await _accountService.GetAccountAsync();
        Assert.Equal(originalLastSync, refreshed!.LastSyncAt);
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

// Returns 500 for everything including health — simulates completely unreachable server → NoServer
public class ErrorHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
}

// Health passes, all entity syncs fail → SyncResult.Failed
public class EntitySyncErrorHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri!.PathAndQuery.Contains("health"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { status = "ok" })
            });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }
}

// Succeeds for health + journal, fails (500) for goal — simulates partial sync failure
public class GoalFailureHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri!.PathAndQuery.Contains("goal"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        if (request.RequestUri.PathAndQuery.Contains("journal"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new SyncResponseDto<JournalSyncDto>([]))
            });
        // health + other entities → 200 empty
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { Records = Array.Empty<object>() })
        });
    }
}

// Returns 500 on the first entity sync call, then 200 on retry — tests retry logic
public class TransientFailThenSucceedHandler : HttpMessageHandler
{
    private int _entityCallCount;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri!.PathAndQuery.Contains("health"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { status = "ok" })
            });

        _entityCallCount++;
        if (_entityCallCount == 1)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SyncResponseDto<JournalSyncDto>([]))
        });
    }
}

public class FakeSyncHandler(JournalSyncDto journal) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri!.PathAndQuery.Contains("journal"))
        {
            var response = new SyncResponseDto<JournalSyncDto>([journal]);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(response)
            });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { Records = Array.Empty<object>() })
        });
    }
}

// Captures request bodies for later inspection; always returns 200 with empty delta
public class CapturingHandler : HttpMessageHandler
{
    private readonly Dictionary<string, string> _bodies = new();

    public string? GetBodyFor(string pathSegment) =>
        _bodies.FirstOrDefault(kv => kv.Key.Contains(pathSegment)).Value;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            _bodies[request.RequestUri!.PathAndQuery] = body;
        }
        if (request.RequestUri!.PathAndQuery.Contains("health"))
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { status = "ok" })
            };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { Records = Array.Empty<object>() })
        };
    }
}
