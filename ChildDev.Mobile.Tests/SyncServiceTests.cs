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
    public async Task RunAsync_ServerUrlSetButJwtMissing_ReturnsNoServer()
    {
        await _accountService.CreateAccountAsync("user17", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        // ServerJwt intentionally left null/empty

        var service = BuildSyncService(new NotCalledHandler());
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.NoServer, result);
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
    public async Task RunAsync_ServerReturnsNullRecords_DoesNotThrow()
    {
        await _accountService.CreateAccountAsync("user2b", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        // Server returns {"Records": null} — should not throw NullReferenceException
        var service = BuildSyncService(new NullRecordsHandler());
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
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

    [Fact]
    public async Task RunAsync_LocalSoftDeletedJournal_IncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user18", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        // Insert a soft-deleted journal directly (GetModifiedSinceAsync must return it)
        var journalGuid = System.Guid.NewGuid().ToString();
        var deletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new Journal
        {
            Guid = journalGuid,
            AccountFk = account.Guid,
            Notes = "deleted note",
            EnteredDate = deletedAt,
            UpdatedOn = deletedAt,
            DeletedAt = deletedAt
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
    public async Task RunAsync_LocalSoftDeletedGoal_IncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user19", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var goalGuid = System.Guid.NewGuid().ToString();
        var deletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = goalGuid, AccountFk = account.Guid, GoalText = "deleted goal",
            EnteredDate = deletedAt, UpdatedOn = deletedAt, DeletedAt = deletedAt
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var goalBody = capturingHandler.GetBodyFor("sync/goal");
        Assert.NotNull(goalBody);
        Assert.Contains(goalGuid, goalBody);
    }

    [Fact]
    public async Task RunAsync_LocalSoftDeletedTodo_IncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user20", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var todoGuid = System.Guid.NewGuid().ToString();
        var deletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new Todo
        {
            Guid = todoGuid, AccountFk = account.Guid, Title = "deleted todo",
            UpdatedOn = deletedAt, DeletedAt = deletedAt
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var todoBody = capturingHandler.GetBodyFor("sync/todo");
        Assert.NotNull(todoBody);
        Assert.Contains(todoGuid, todoBody);
    }

    [Fact]
    public async Task RunAsync_LocalSoftDeletedGoalProgress_IncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user21", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var progressGuid = System.Guid.NewGuid().ToString();
        var goalFk = System.Guid.NewGuid().ToString();
        var deletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = progressGuid, AccountFk = account.Guid, GoalFk = goalFk,
            NextStepItems = "deleted step", UpdatedOn = deletedAt, DeletedAt = deletedAt
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var progressBody = capturingHandler.GetBodyFor("sync/goal-progress");
        Assert.NotNull(progressBody);
        Assert.Contains(progressGuid, progressBody);
    }

    [Fact]
    public async Task RunAsync_LocalGoalModifiedSinceLastSync_IncludedInRequest()
    {
        await _accountService.CreateAccountAsync("user10", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var goalGuid = System.Guid.NewGuid().ToString();
        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = goalGuid,
            AccountFk = account.Guid,
            GoalText = "local goal",
            EnteredDate = updatedOn,
            UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var goalBody = capturingHandler.GetBodyFor("sync/goal");
        Assert.NotNull(goalBody);
        Assert.Contains(goalGuid, goalBody);
    }

    [Fact]
    public async Task RunAsync_LocalTodoModifiedSinceLastSync_IncludedInRequest()
    {
        await _accountService.CreateAccountAsync("user11", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var todoGuid = System.Guid.NewGuid().ToString();
        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new Todo
        {
            Guid = todoGuid,
            AccountFk = account.Guid,
            Title = "local todo",
            UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var todoBody = capturingHandler.GetBodyFor("sync/todo");
        Assert.NotNull(todoBody);
        Assert.Contains(todoGuid, todoBody);
    }

    [Fact]
    public async Task RunAsync_LocalGoalProgressModifiedSinceLastSync_IncludedInRequest()
    {
        await _accountService.CreateAccountAsync("user9", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var progressGuid = System.Guid.NewGuid().ToString();
        var goalFk = System.Guid.NewGuid().ToString();
        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = progressGuid,
            AccountFk = account.Guid,
            GoalFk = goalFk,
            NextStepItems = "local step",
            UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var progressBody = capturingHandler.GetBodyFor("sync/goal-progress");
        Assert.NotNull(progressBody);
        Assert.Contains(progressGuid, progressBody);
    }

    [Fact]
    public async Task RunAsync_SecondSync_SendsLastSyncAtFromPriorSync()
    {
        await _accountService.CreateAccountAsync("user16", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        // First sync — sets LastSyncAt
        var firstHandler = new CapturingHandler();
        var service = BuildSyncService(firstHandler);
        await service.RunAsync(account);

        account = await _accountService.GetAccountAsync();
        var firstLastSyncAt = account!.LastSyncAt;
        Assert.True(firstLastSyncAt > 0);

        // Second sync — the request body must contain the persisted LastSyncAt value
        account.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";
        var secondHandler = new CapturingHandler();
        var service2 = BuildSyncService(secondHandler);
        await service2.RunAsync(account);

        var journalBody = secondHandler.GetBodyFor("sync/journal");
        Assert.NotNull(journalBody);
        Assert.Contains(firstLastSyncAt.ToString(), journalBody);
    }

    [Fact]
    public async Task RunAsync_FailedSync_ReleasesLockSoSubsequentSyncCanRun()
    {
        await _accountService.CreateAccountAsync("user15", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var service = BuildSyncService(new EntitySyncErrorHandler());
        var firstResult = await service.RunAsync(account);
        Assert.Equal(SyncResult.Failed, firstResult);

        // If the finally block didn't release the lock, the guard would return Success immediately.
        // Returning Failed here proves the second call actually ran (lock was freed).
        var secondResult = await service.RunAsync(account);
        Assert.Equal(SyncResult.Failed, secondResult);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsGoal_UpsertsLocally()
    {
        await _accountService.CreateAccountAsync("user12", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var serverGoal = new GoalSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, "From server goal",
            null, null, 1000, null, null, 1000, null);

        var handler = new FakeGoalSyncHandler(serverGoal);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var goals = await _goalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(goals);
        Assert.Equal("From server goal", goals[0].GoalText);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsTodo_UpsertsLocally()
    {
        await _accountService.CreateAccountAsync("user13", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var serverTodo = new TodoSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, "From server todo",
            null, null, null, 1000, null);

        var handler = new FakeTodoSyncHandler(serverTodo);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var todos = await _todoRepo.GetPendingAsync(account.Guid);
        Assert.Single(todos);
        Assert.Equal("From server todo", todos[0].Title);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsGoalProgress_UpsertsLocally()
    {
        await _accountService.CreateAccountAsync("user14", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var goalFk = System.Guid.NewGuid().ToString();
        var serverProgress = new GoalProgressSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, goalFk,
            "From server step", null, 1000, null);

        var handler = new FakeGoalProgressSyncHandler(serverProgress);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var items = await _goalProgressRepo.GetForGoalAsync(goalFk);
        Assert.Single(items);
        Assert.Equal("From server step", items[0].NextStepItems);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsDeletedJournal_DeletedAtPropagatedLocally()
    {
        await _accountService.CreateAccountAsync("user22", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var deletedAt = 5_000_000L;
        var serverJournal = new JournalSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, null,
            null, null, null, deletedAt, deletedAt, deletedAt);

        var handler = new FakeSyncHandler(serverJournal);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var stored = await _db.FindAsync<Journal>(serverJournal.Guid);
        Assert.NotNull(stored);
        Assert.Equal(deletedAt, stored!.DeletedAt);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsDeletedGoal_DeletedAtPropagatedLocally()
    {
        await _accountService.CreateAccountAsync("user23", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var deletedAt = 5_000_000L;
        var serverGoal = new GoalSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, null,
            null, null, deletedAt, null, null, deletedAt, deletedAt);

        var handler = new FakeGoalSyncHandler(serverGoal);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var stored = await _db.FindAsync<Goal>(serverGoal.Guid);
        Assert.NotNull(stored);
        Assert.Equal(deletedAt, stored!.DeletedAt);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsDeletedTodo_DeletedAtPropagatedLocally()
    {
        await _accountService.CreateAccountAsync("user24", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var deletedAt = 5_000_000L;
        var serverTodo = new TodoSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, null,
            null, null, null, deletedAt, deletedAt);

        var handler = new FakeTodoSyncHandler(serverTodo);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var stored = await _db.FindAsync<Todo>(serverTodo.Guid);
        Assert.NotNull(stored);
        Assert.Equal(deletedAt, stored!.DeletedAt);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsDeletedGoalProgress_DeletedAtPropagatedLocally()
    {
        await _accountService.CreateAccountAsync("user25", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var deletedAt = 5_000_000L;
        var goalFk = System.Guid.NewGuid().ToString();
        var serverProgress = new GoalProgressSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, goalFk,
            null, null, deletedAt, deletedAt);

        var handler = new FakeGoalProgressSyncHandler(serverProgress);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var stored = await _db.FindAsync<GoalProgress>(serverProgress.Guid);
        Assert.NotNull(stored);
        Assert.Equal(deletedAt, stored!.DeletedAt);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsCompletedGoal_CompletionDateStoredLocally()
    {
        await _accountService.CreateAccountAsync("user27", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var completionDate = 5_000_000L;
        var serverGoal = new GoalSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, "Completed goal",
            null, null, completionDate, null, completionDate, completionDate, null);

        var handler = new FakeGoalSyncHandler(serverGoal);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var stored = await _db.FindAsync<Goal>(serverGoal.Guid);
        Assert.NotNull(stored);
        Assert.Equal(completionDate, stored!.CompletionDate);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsCompletedTodo_CompletedAtStoredLocally()
    {
        await _accountService.CreateAccountAsync("user28", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var completedAt = 5_000_000L;
        var serverTodo = new TodoSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, "Completed task",
            null, null, completedAt, completedAt, null);

        var handler = new FakeTodoSyncHandler(serverTodo);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var stored = await _db.FindAsync<Todo>(serverTodo.Guid);
        Assert.NotNull(stored);
        Assert.Equal(completedAt, stored!.CompletedAt);
    }

    [Fact]
    public async Task RunAsync_EntitySyncNetworkError_RetriesAndSucceeds()
    {
        await _accountService.CreateAccountAsync("user26", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var service = BuildSyncService(new NetworkErrorThenSucceedHandler());
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsJournal_AuxFieldsStoredLocally()
    {
        await _accountService.CreateAccountAsync("user29", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var serverJournal = new JournalSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, "A note",
            "Coding", "Happy", "work,dev", 1000, 1000, null);

        var handler = new FakeSyncHandler(serverJournal);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var stored = await _db.FindAsync<Journal>(serverJournal.Guid);
        Assert.NotNull(stored);
        Assert.Equal("Coding", stored!.Activity);
        Assert.Equal("Happy", stored.Mood);
        Assert.Equal("work,dev", stored.Tags);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsGoalProgress_NextMeetingDateStoredLocally()
    {
        await _accountService.CreateAccountAsync("user30", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var goalFk = System.Guid.NewGuid().ToString();
        var nextMeeting = 9_000_000L;
        var serverProgress = new GoalProgressSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, goalFk,
            "Next steps", nextMeeting, 1000, null);

        var handler = new FakeGoalProgressSyncHandler(serverProgress);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var stored = await _db.FindAsync<GoalProgress>(serverProgress.Guid);
        Assert.NotNull(stored);
        Assert.Equal(nextMeeting, stored!.NextMeetingDate);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsTodo_DueDateAndNotesStoredLocally()
    {
        await _accountService.CreateAccountAsync("user31", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var dueDate = 8_000_000L;
        var serverTodo = new TodoSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, "Buy groceries",
            "Milk and eggs", dueDate, null, 1000, null);

        var handler = new FakeTodoSyncHandler(serverTodo);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var stored = await _db.FindAsync<Todo>(serverTodo.Guid);
        Assert.NotNull(stored);
        Assert.Equal("Milk and eggs", stored!.Notes);
        Assert.Equal(dueDate, stored.DueDate);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsGoal_OptionalFieldsStoredLocally()
    {
        await _accountService.CreateAccountAsync("user32", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var nextMeeting = 7_000_000L;
        var expiration = 8_000_000L;
        var serverGoal = new GoalSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, "Learn piano",
            nextMeeting, expiration, 1000, "Play one song fluently", null, 1000, null);

        var handler = new FakeGoalSyncHandler(serverGoal);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var stored = await _db.FindAsync<Goal>(serverGoal.Guid);
        Assert.NotNull(stored);
        Assert.Equal(nextMeeting, stored!.NextMeetingDate);
        Assert.Equal(expiration, stored.ExpirationDate);
        Assert.Equal("Play one song fluently", stored.MeasurableOutcome);
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

// Throws HttpRequestException on first entity sync call, then succeeds on retry
public class NetworkErrorThenSucceedHandler : HttpMessageHandler
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
            throw new HttpRequestException("Simulated network error");

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SyncResponseDto<JournalSyncDto>([]))
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

public class FakeGoalSyncHandler(GoalSyncDto goal) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri!.PathAndQuery.Contains("sync/goal") &&
            !request.RequestUri.PathAndQuery.Contains("goal-progress"))
        {
            var response = new SyncResponseDto<GoalSyncDto>([goal]);
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

public class FakeTodoSyncHandler(TodoSyncDto todo) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri!.PathAndQuery.Contains("sync/todo"))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new SyncResponseDto<TodoSyncDto>([todo]))
            });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { Records = Array.Empty<object>() })
        });
    }
}

public class FakeGoalProgressSyncHandler(GoalProgressSyncDto progress) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri!.PathAndQuery.Contains("sync/goal-progress"))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new SyncResponseDto<GoalProgressSyncDto>([progress]))
            });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { Records = Array.Empty<object>() })
        });
    }
}

// Returns {"Records": null} for entity syncs — tests null safety in SyncEntityAsync
public class NullRecordsHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri!.PathAndQuery.Contains("health"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { status = "ok" })
            });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"Records\":null}", System.Text.Encoding.UTF8, "application/json")
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
