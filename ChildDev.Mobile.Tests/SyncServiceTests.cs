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
    public async Task RunAsync_ServerReturnsGoalProgress_NullNextStepsMeetingDateOnly_StoredLocally()
    {
        await _accountService.CreateAccountAsync("user30b", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var goalFk = System.Guid.NewGuid().ToString();
        var nextMeeting = 9_500_000L;
        var serverProgress = new GoalProgressSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, goalFk,
            null, nextMeeting, 1000, null);

        var handler = new FakeGoalProgressSyncHandler(serverProgress);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var stored = await _db.FindAsync<GoalProgress>(serverProgress.Guid);
        Assert.NotNull(stored);
        Assert.Null(stored!.NextStepItems);
        Assert.Equal(nextMeeting, stored.NextMeetingDate);
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

    [Fact]
    public async Task RunAsync_LocalJournal_AuxFieldsIncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user33", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new Journal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Notes = "a note", Activity = "Yoga", Mood = "Calm", Tags = "health,wellness",
            EnteredDate = updatedOn, UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        await service.RunAsync(account);

        var body = capturingHandler.GetBodyFor("sync/journal");
        Assert.NotNull(body);
        Assert.Contains("Yoga", body);
        Assert.Contains("Calm", body);
        Assert.Contains("health,wellness", body);
    }

    [Fact]
    public async Task RunAsync_LocalGoal_OptionalFieldsIncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user34", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nextMeeting = updatedOn + 1_000_000L;
        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Run a marathon", MeasurableOutcome = "Complete 42km",
            NextMeetingDate = nextMeeting, EnteredDate = updatedOn, UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        await service.RunAsync(account);

        var body = capturingHandler.GetBodyFor("sync/goal");
        Assert.NotNull(body);
        Assert.Contains("Complete 42km", body);
        Assert.Contains(nextMeeting.ToString(), body);
    }

    [Fact]
    public async Task RunAsync_LocalTodo_DueDateIncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user35", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var dueDate = updatedOn + 86_400_000L;
        await _db.InsertOrReplaceAsync(new Todo
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Title = "Buy milk", DueDate = dueDate, UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        await service.RunAsync(account);

        var body = capturingHandler.GetBodyFor("sync/todo");
        Assert.NotNull(body);
        Assert.Contains(dueDate.ToString(), body);
    }

    [Fact]
    public async Task RunAsync_LocalGoalProgress_NextMeetingDateIncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user36", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nextMeeting = updatedOn + 2_000_000L;
        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalFk = System.Guid.NewGuid().ToString(), NextStepItems = "draft plan",
            NextMeetingDate = nextMeeting, UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        await service.RunAsync(account);

        var body = capturingHandler.GetBodyFor("sync/goal-progress");
        Assert.NotNull(body);
        Assert.Contains(nextMeeting.ToString(), body);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsExistingJournal_OverwritesLocalVersion()
    {
        await _accountService.CreateAccountAsync("user39", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var journalGuid = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Pre-insert a journal locally with an older timestamp
        await _db.InsertOrReplaceAsync(new Journal
        {
            Guid = journalGuid, AccountFk = account.Guid, Notes = "local version",
            EnteredDate = now, UpdatedOn = now
        });

        // Server returns same Guid with newer UpdatedOn and updated Notes
        var serverJournal = new JournalSyncDto(journalGuid, account.Guid, "server version",
            null, null, null, now, now + 1000, null);

        var handler = new FakeSyncHandler(serverJournal);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var retrieved = await _journalRepo.GetAsync(journalGuid);
        Assert.NotNull(retrieved);
        Assert.Equal("server version", retrieved!.Notes);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsExistingGoal_OverwritesLocalVersion()
    {
        await _accountService.CreateAccountAsync("user40", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var goalGuid = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = goalGuid, AccountFk = account.Guid, GoalText = "local goal",
            EnteredDate = now, UpdatedOn = now
        });

        var serverGoal = new GoalSyncDto(goalGuid, account.Guid, "server goal",
            null, null, now, null, null, now + 1000, null);

        var handler = new FakeGoalSyncHandler(serverGoal);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var retrieved = await _goalRepo.GetAsync(goalGuid);
        Assert.NotNull(retrieved);
        Assert.Equal("server goal", retrieved!.GoalText);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsExistingTodo_OverwritesLocalVersion()
    {
        await _accountService.CreateAccountAsync("user41", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var todoGuid = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Todo
        {
            Guid = todoGuid, AccountFk = account.Guid, Title = "local task", UpdatedOn = now
        });

        var serverTodo = new TodoSyncDto(todoGuid, account.Guid, "server task",
            null, null, null, now + 1000, null);

        var handler = new FakeTodoSyncHandler(serverTodo);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var retrieved = await _todoRepo.GetAsync(todoGuid);
        Assert.NotNull(retrieved);
        Assert.Equal("server task", retrieved!.Title);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsExistingGoalProgress_OverwritesLocalVersion()
    {
        await _accountService.CreateAccountAsync("user42", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var progressGuid = System.Guid.NewGuid().ToString();
        var goalFk = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = progressGuid, AccountFk = account.Guid, GoalFk = goalFk,
            NextStepItems = "local steps", UpdatedOn = now
        });

        var serverProgress = new GoalProgressSyncDto(progressGuid, account.Guid, goalFk,
            "server steps", null, now + 1000, null);

        var handler = new FakeGoalProgressSyncHandler(serverProgress);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var retrieved = await _db.FindAsync<GoalProgress>(progressGuid);
        Assert.NotNull(retrieved);
        Assert.Equal("server steps", retrieved!.NextStepItems);
    }

    [Fact]
    public async Task RunAsync_NoLocalChanges_SendsEmptyBatchToAllEndpoints()
    {
        await _accountService.CreateAccountAsync("user38", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var journalBody = capturingHandler.GetBodyFor("sync/journal");
        Assert.NotNull(journalBody);
        Assert.Contains("\"records\":[]", journalBody);
    }

    [Fact]
    public async Task RunAsync_MultipleLocalModifications_AllFourEndpointsReceiveData()
    {
        await _accountService.CreateAccountAsync("user37", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goalGuid = System.Guid.NewGuid().ToString();

        await _db.InsertOrReplaceAsync(new Journal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "a note", EnteredDate = updatedOn, UpdatedOn = updatedOn });
        await _db.InsertOrReplaceAsync(new Goal { Guid = goalGuid, AccountFk = account.Guid, GoalText = "a goal", EnteredDate = updatedOn, UpdatedOn = updatedOn });
        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goalGuid, NextStepItems = "step", UpdatedOn = updatedOn });
        await _db.InsertOrReplaceAsync(new Todo { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "a todo", UpdatedOn = updatedOn });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        Assert.NotNull(capturingHandler.GetBodyFor("sync/journal"));
        Assert.NotNull(capturingHandler.GetBodyFor("sync/goal"));
        Assert.NotNull(capturingHandler.GetBodyFor("sync/goal-progress"));
        Assert.NotNull(capturingHandler.GetBodyFor("sync/todo"));
    }

    [Fact]
    public async Task RunAsync_LocalGoal_ExpirationDateIncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user45", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expirationDate = updatedOn + 2_592_000_000L;
        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "expire soon", ExpirationDate = expirationDate,
            EnteredDate = updatedOn, UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        await service.RunAsync(account);

        var body = capturingHandler.GetBodyFor("sync/goal");
        Assert.NotNull(body);
        Assert.Contains(expirationDate.ToString(), body);
    }

    [Fact]
    public async Task RunAsync_LocalCompletedGoal_CompletionDateIncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user44", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var goalGuid = System.Guid.NewGuid().ToString();
        var completionDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = goalGuid, AccountFk = account.Guid, GoalText = "done goal",
            EnteredDate = completionDate, UpdatedOn = completionDate, CompletionDate = completionDate
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var goalBody = capturingHandler.GetBodyFor("sync/goal");
        Assert.NotNull(goalBody);
        Assert.Contains(goalGuid, goalBody);
        Assert.Contains(completionDate.ToString(), goalBody);
    }

    [Fact]
    public async Task RunAsync_LocalGoalProgress_GoalFkIncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user55", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var goalFk = System.Guid.NewGuid().ToString();
        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalFk = goalFk, NextStepItems = "Next steps", UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        await service.RunAsync(account);

        var body = capturingHandler.GetBodyFor("sync/goal-progress");
        Assert.NotNull(body);
        Assert.Contains(goalFk, body);
    }

    [Fact]
    public async Task RunAsync_LocalTodo_TitleIncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user54", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new Todo
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Title = "Call the dentist", UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        await service.RunAsync(account);

        var body = capturingHandler.GetBodyFor("sync/todo");
        Assert.NotNull(body);
        Assert.Contains("Call the dentist", body);
    }

    [Fact]
    public async Task RunAsync_LocalJournal_EnteredDateIncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user53", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var enteredDate = 3_000_000L;
        var updatedOn = 4_000_000L;
        await _db.InsertOrReplaceAsync(new Journal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Notes = "journal note", EnteredDate = enteredDate, UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        await service.RunAsync(account);

        var body = capturingHandler.GetBodyFor("sync/journal");
        Assert.NotNull(body);
        Assert.Contains(enteredDate.ToString(), body);
    }

    [Fact]
    public async Task RunAsync_LocalGoal_EnteredDateIncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user52", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var enteredDate = 1_000_000L;
        var updatedOn = 2_000_000L;
        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Read 12 books", EnteredDate = enteredDate, UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        await service.RunAsync(account);

        var body = capturingHandler.GetBodyFor("sync/goal");
        Assert.NotNull(body);
        Assert.Contains(enteredDate.ToString(), body);
    }

    [Fact]
    public async Task RunAsync_LocalJournal_NotesIncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user51", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new Journal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Notes = "Reflection on the week", EnteredDate = updatedOn, UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        await service.RunAsync(account);

        var body = capturingHandler.GetBodyFor("sync/journal");
        Assert.NotNull(body);
        Assert.Contains("Reflection on the week", body);
    }

    [Fact]
    public async Task RunAsync_LocalGoal_GoalTextIncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user50", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Master the piano", EnteredDate = updatedOn, UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        await service.RunAsync(account);

        var body = capturingHandler.GetBodyFor("sync/goal");
        Assert.NotNull(body);
        Assert.Contains("Master the piano", body);
    }

    [Fact]
    public async Task RunAsync_LocalGoalProgress_NextStepItemsIncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user49", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalFk = System.Guid.NewGuid().ToString(),
            NextStepItems = "Write unit tests daily", UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        await service.RunAsync(account);

        var body = capturingHandler.GetBodyFor("sync/goal-progress");
        Assert.NotNull(body);
        Assert.Contains("Write unit tests daily", body);
    }

    [Fact]
    public async Task RunAsync_LocalTodo_NotesIncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user48", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new Todo
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Title = "Do laundry", Notes = "Use cold water", UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        await service.RunAsync(account);

        var body = capturingHandler.GetBodyFor("sync/todo");
        Assert.NotNull(body);
        Assert.Contains("Use cold water", body);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsGoal_EnteredDateStoredLocally()
    {
        await _accountService.CreateAccountAsync("user47", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var enteredDate = 3_000_000L;
        var serverGoal = new GoalSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, "My goal",
            null, null, enteredDate, null, null, enteredDate, null);

        var handler = new FakeGoalSyncHandler(serverGoal);
        var service = BuildSyncService(handler);
        await service.RunAsync(account);

        var stored = await _db.FindAsync<Goal>(serverGoal.Guid);
        Assert.NotNull(stored);
        Assert.Equal(enteredDate, stored!.EnteredDate);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsJournal_EnteredDateStoredLocally()
    {
        await _accountService.CreateAccountAsync("user46", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var enteredDate = 5_000_000L;
        var serverJournal = new JournalSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, "Entry note",
            null, null, null, enteredDate, enteredDate, null);

        var handler = new FakeSyncHandler(serverJournal);
        var service = BuildSyncService(handler);
        await service.RunAsync(account);

        var stored = await _db.FindAsync<Journal>(serverJournal.Guid);
        Assert.NotNull(stored);
        Assert.Equal(enteredDate, stored!.EnteredDate);
    }

    [Fact]
    public async Task RunAsync_LocalCompletedTodo_CompletedAtIncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user43", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var todoGuid = System.Guid.NewGuid().ToString();
        var completedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new Todo
        {
            Guid = todoGuid, AccountFk = account.Guid, Title = "done task",
            UpdatedOn = completedAt, CompletedAt = completedAt
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var todoBody = capturingHandler.GetBodyFor("sync/todo");
        Assert.NotNull(todoBody);
        Assert.Contains(todoGuid, todoBody);
        Assert.Contains(completedAt.ToString(), todoBody);
    }

    [Fact]
    public async Task RunAsync_HealthCheckTimeout_ReturnsNoServer()
    {
        await _accountService.CreateAccountAsync("user56", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var service = BuildSyncService(new SlowHealthHandler());
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.NoServer, result);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsJournal_UpdatedOnStoredLocally()
    {
        await _accountService.CreateAccountAsync("user57", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var serverUpdatedOn = 9_000_000L;
        var serverJournal = new JournalSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, "server note",
            null, null, null, serverUpdatedOn, serverUpdatedOn, null);

        var service = BuildSyncService(new FakeSyncHandler(serverJournal));
        await service.RunAsync(account);

        var stored = await _db.FindAsync<Journal>(serverJournal.Guid);
        Assert.NotNull(stored);
        Assert.Equal(serverUpdatedOn, stored!.UpdatedOn);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsTodo_UpdatedOnStoredLocally()
    {
        await _accountService.CreateAccountAsync("user58", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var serverUpdatedOn = 9_100_000L;
        var serverTodo = new TodoSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, "server task",
            null, null, null, serverUpdatedOn, null);

        var service = BuildSyncService(new FakeTodoSyncHandler(serverTodo));
        await service.RunAsync(account);

        var stored = await _db.FindAsync<Todo>(serverTodo.Guid);
        Assert.NotNull(stored);
        Assert.Equal(serverUpdatedOn, stored!.UpdatedOn);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsGoalProgress_UpdatedOnStoredLocally()
    {
        await _accountService.CreateAccountAsync("user59", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var serverUpdatedOn = 9_200_000L;
        var serverProgress = new GoalProgressSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, System.Guid.NewGuid().ToString(),
            "server steps", null, serverUpdatedOn, null);

        var service = BuildSyncService(new FakeGoalProgressSyncHandler(serverProgress));
        await service.RunAsync(account);

        var stored = await _db.FindAsync<GoalProgress>(serverProgress.Guid);
        Assert.NotNull(stored);
        Assert.Equal(serverUpdatedOn, stored!.UpdatedOn);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsGoal_UpdatedOnStoredLocally()
    {
        await _accountService.CreateAccountAsync("user60", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var serverUpdatedOn = 9_300_000L;
        var serverGoal = new GoalSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, "server goal",
            null, null, serverUpdatedOn, null, null, serverUpdatedOn, null);

        var service = BuildSyncService(new FakeGoalSyncHandler(serverGoal));
        await service.RunAsync(account);

        var stored = await _db.FindAsync<Goal>(serverGoal.Guid);
        Assert.NotNull(stored);
        Assert.Equal(serverUpdatedOn, stored!.UpdatedOn);
    }

    [Fact]
    public async Task RunAsync_LocalJournal_AccountFkIncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user61", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new Journal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Notes = "test note", EnteredDate = updatedOn, UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        await service.RunAsync(account);

        var journalBody = capturingHandler.GetBodyFor("sync/journal");
        Assert.NotNull(journalBody);
        Assert.Contains(account.Guid, journalBody);
    }

    [Fact]
    public async Task RunAsync_LocalGoal_AccountFkIncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user62", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "test goal", EnteredDate = updatedOn, UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        await service.RunAsync(account);

        var goalBody = capturingHandler.GetBodyFor("sync/goal");
        Assert.NotNull(goalBody);
        Assert.Contains(account.Guid, goalBody);
    }

    [Fact]
    public async Task RunAsync_LocalTodo_AccountFkIncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user63", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new Todo
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Title = "test todo", UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        await service.RunAsync(account);

        var todoBody = capturingHandler.GetBodyFor("sync/todo");
        Assert.NotNull(todoBody);
        Assert.Contains(account.Guid, todoBody);
    }

    [Fact]
    public async Task RunAsync_LocalGoalProgress_AccountFkIncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user64", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalFk = System.Guid.NewGuid().ToString(),
            NextStepItems = "test steps", UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        await service.RunAsync(account);

        var progressBody = capturingHandler.GetBodyFor("sync/goal-progress");
        Assert.NotNull(progressBody);
        Assert.Contains(account.Guid, progressBody);
    }

    [Fact]
    public async Task RunAsync_ServerSendsDeletedGoalProgress_ExcludedFromGetForGoalAsync()
    {
        await _accountService.CreateAccountAsync("user72", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var progressGuid = System.Guid.NewGuid().ToString();
        var goalFk = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Pre-insert active progress for the goal
        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = progressGuid, AccountFk = account.Guid, GoalFk = goalFk,
            NextStepItems = "Active steps", UpdatedOn = now
        });

        // Confirm it's visible before sync
        var progressBefore = await _goalProgressRepo.GetForGoalAsync(goalFk);
        Assert.Contains(progressBefore, p => p.Guid == progressGuid);

        // Server sends same Guid soft-deleted with newer UpdatedOn
        var deletedAt = now + 1000;
        var serverProgress = new GoalProgressSyncDto(progressGuid, account.Guid, goalFk,
            null, null, deletedAt, deletedAt);

        var service = BuildSyncService(new FakeGoalProgressSyncHandler(serverProgress));
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var progressAfter = await _goalProgressRepo.GetForGoalAsync(goalFk);
        Assert.DoesNotContain(progressAfter, p => p.Guid == progressGuid);
    }

    [Fact]
    public async Task RunAsync_ServerSendsDeletedGoal_ExcludedFromGetAllActiveAsync()
    {
        await _accountService.CreateAccountAsync("user71", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var goalGuid = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Pre-insert the goal locally as active
        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = goalGuid, AccountFk = account.Guid, GoalText = "Active goal",
            EnteredDate = now, UpdatedOn = now
        });

        // Confirm active before sync
        var activeBefore = await _goalRepo.GetAllActiveAsync(account.Guid);
        Assert.Contains(activeBefore, g => g.Guid == goalGuid);

        // Server sends same Guid soft-deleted with newer UpdatedOn
        var deletedAt = now + 1000;
        var serverGoal = new GoalSyncDto(goalGuid, account.Guid, null,
            null, null, now, null, null, deletedAt, deletedAt);

        var service = BuildSyncService(new FakeGoalSyncHandler(serverGoal));
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var activeAfter = await _goalRepo.GetAllActiveAsync(account.Guid);
        Assert.DoesNotContain(activeAfter, g => g.Guid == goalGuid);
    }

    [Fact]
    public async Task RunAsync_ServerSendsDeletedJournal_ExcludedFromGetAllActiveAsync()
    {
        await _accountService.CreateAccountAsync("user70", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var journalGuid = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Pre-insert the journal locally as active
        await _db.InsertOrReplaceAsync(new Journal
        {
            Guid = journalGuid, AccountFk = account.Guid, Notes = "Active journal",
            EnteredDate = now, UpdatedOn = now
        });

        // Verify it's active before sync
        var activeBefore = await _journalRepo.GetAllActiveAsync(account.Guid);
        Assert.Contains(activeBefore, j => j.Guid == journalGuid);

        // Server sends same Guid with DeletedAt set and newer UpdatedOn — journal is soft-deleted
        var deletedAt = now + 1000;
        var serverJournal = new JournalSyncDto(journalGuid, account.Guid, null,
            null, null, null, now, deletedAt, deletedAt);

        var service = BuildSyncService(new FakeSyncHandler(serverJournal));
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var activeAfter = await _journalRepo.GetAllActiveAsync(account.Guid);
        Assert.DoesNotContain(activeAfter, j => j.Guid == journalGuid);
    }

    [Fact]
    public async Task RunAsync_ServerSendsDeletedTodo_ExcludedFromGetPendingAsync()
    {
        await _accountService.CreateAccountAsync("user69", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var todoGuid = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Pre-insert the todo locally as active (pending)
        await _db.InsertOrReplaceAsync(new Todo
        {
            Guid = todoGuid, AccountFk = account.Guid, Title = "Active task", UpdatedOn = now
        });

        // Verify it's pending before sync
        var pendingBefore = await _todoRepo.GetPendingAsync(account.Guid);
        Assert.Contains(pendingBefore, t => t.Guid == todoGuid);

        // Server sends same Guid with DeletedAt set and newer UpdatedOn — todo is soft-deleted
        var deletedAt = now + 1000;
        var serverTodo = new TodoSyncDto(todoGuid, account.Guid, null,
            null, null, null, deletedAt, deletedAt);

        var service = BuildSyncService(new FakeTodoSyncHandler(serverTodo));
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var pendingAfter = await _todoRepo.GetPendingAsync(account.Guid);
        Assert.DoesNotContain(pendingAfter, t => t.Guid == todoGuid);
    }

    [Fact]
    public async Task RunAsync_ServerSendsGoalWithNullCompletionDate_GoalAppearsActiveLocally()
    {
        await _accountService.CreateAccountAsync("user68", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var goalGuid = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Insert a locally-completed goal directly
        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = goalGuid, AccountFk = account.Guid, GoalText = "completed locally",
            EnteredDate = now, UpdatedOn = now, CompletionDate = now
        });

        // Server sends same goal with newer UpdatedOn and CompletionDate = null — goal is un-completed
        var serverGoal = new GoalSyncDto(goalGuid, account.Guid, "un-completed goal",
            null, null, now, null, null, now + 1000, null);

        var service = BuildSyncService(new FakeGoalSyncHandler(serverGoal));
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var activeGoals = await _goalRepo.GetAllActiveAsync(account.Guid);
        var stored = activeGoals.FirstOrDefault(g => g.Guid == goalGuid);
        Assert.NotNull(stored);
        Assert.Null(stored!.CompletionDate);
    }

    [Fact]
    public async Task RunAsync_BearerJwt_IncludedInRequestHeaders()
    {
        await _accountService.CreateAccountAsync("user67", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "my-secret-jwt";

        var handler = new AuthHeaderCapturingHandler();
        var service = BuildSyncService(handler);
        await service.RunAsync(account);

        // The JWT must be sent as a Bearer token on every request including entity syncs
        Assert.NotNull(handler.CapturedAuthHeader);
        Assert.Equal("Bearer", handler.CapturedAuthHeader.Scheme);
        Assert.Equal("my-secret-jwt", handler.CapturedAuthHeader.Parameter);
    }

    [Fact]
    public async Task RunAsync_LastSyncAt_IncludedInAllFourEntityBodies()
    {
        await _accountService.CreateAccountAsync("user66", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var knownLastSync = 12_345_678L;
        await _accountService.UpdateLastSyncAsync(knownLastSync);
        account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        await service.RunAsync(account);

        var journalBody = capturingHandler.GetBodyFor("sync/journal");
        var goalBody = capturingHandler.GetBodyFor("sync/goal");
        var progressBody = capturingHandler.GetBodyFor("sync/goal-progress");
        var todoBody = capturingHandler.GetBodyFor("sync/todo");

        Assert.NotNull(journalBody);
        Assert.NotNull(goalBody);
        Assert.NotNull(progressBody);
        Assert.NotNull(todoBody);
        Assert.Contains(knownLastSync.ToString(), journalBody);
        Assert.Contains(knownLastSync.ToString(), goalBody);
        Assert.Contains(knownLastSync.ToString(), progressBody);
        Assert.Contains(knownLastSync.ToString(), todoBody);
    }

    [Fact]
    public async Task RunAsync_EntitySyncNetworkErrorOnBothAttempts_ReturnsFailed()
    {
        await _accountService.CreateAccountAsync("user65", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        // Health succeeds but all entity sync attempts (initial + retry) throw — outer catch → Failed
        var service = BuildSyncService(new AlwaysNetworkErrorEntityHandler());
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Failed, result);
    }

    [Fact]
    public async Task RunAsync_Server401OnEntitySync_ReturnsFailed()
    {
        await _accountService.CreateAccountAsync("user77", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "expired-jwt";

        // Health passes but entity sync returns 401 — EnsureSuccessStatusCode throws → Failed
        var service = BuildSyncService(new EntitySync401Handler());
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Failed, result);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsTwoJournals_BothUpsertedLocally()
    {
        await _accountService.CreateAccountAsync("user80", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var journal1 = new JournalSyncDto(System.Guid.NewGuid().ToString(), account.Guid, "first note",
            null, null, null, 1000, 1000, null);
        var journal2 = new JournalSyncDto(System.Guid.NewGuid().ToString(), account.Guid, "second note",
            null, null, null, 2000, 2000, null);

        var service = BuildSyncService(new MultiJournalSyncHandler(journal1, journal2));
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var journals = await _journalRepo.GetAllActiveAsync(account.Guid);
        Assert.Equal(2, journals.Count);
        Assert.Contains(journals, j => j.Notes == "first note");
        Assert.Contains(journals, j => j.Notes == "second note");
    }

    [Fact]
    public async Task RunAsync_PartialFailure_ReleasesLockSoSubsequentSyncCanRun()
    {
        await _accountService.CreateAccountAsync("user79", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        // GoalFailureHandler: journal succeeds, goal always returns 500 → Failed
        var service = BuildSyncService(new GoalFailureHandler());
        var firstResult = await service.RunAsync(account);
        Assert.Equal(SyncResult.Failed, firstResult);

        // If finally block didn't release the lock after partial failure,
        // the second call would return Success immediately (the lock guard path).
        // Returning Failed here proves it actually ran the sync logic (lock was freed).
        var secondResult = await service.RunAsync(account);
        Assert.Equal(SyncResult.Failed, secondResult);
    }

    [Fact]
    public async Task RunAsync_Server403OnEntitySync_ReturnsFailed()
    {
        await _accountService.CreateAccountAsync("user78", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "forbidden-jwt";

        var service = BuildSyncService(new EntitySync403Handler());
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Failed, result);
    }

    [Fact]
    public async Task RunAsync_LocalSoftDeletedJournal_DeletedAtSerializedInUploadBody()
    {
        await _accountService.CreateAccountAsync("user73", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var deletedAt = 99_000_001L;
        await _db.InsertOrReplaceAsync(new Journal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Notes = "deleted note", EnteredDate = deletedAt, UpdatedOn = deletedAt, DeletedAt = deletedAt
        });

        var capturingHandler = new CapturingHandler();
        await BuildSyncService(capturingHandler).RunAsync(account);

        var body = capturingHandler.GetBodyFor("sync/journal");
        Assert.NotNull(body);
        Assert.Contains(deletedAt.ToString(), body);
    }

    [Fact]
    public async Task RunAsync_LocalSoftDeletedGoal_DeletedAtSerializedInUploadBody()
    {
        await _accountService.CreateAccountAsync("user74", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var deletedAt = 99_000_002L;
        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "deleted goal", EnteredDate = deletedAt, UpdatedOn = deletedAt, DeletedAt = deletedAt
        });

        var capturingHandler = new CapturingHandler();
        await BuildSyncService(capturingHandler).RunAsync(account);

        var body = capturingHandler.GetBodyFor("sync/goal");
        Assert.NotNull(body);
        Assert.Contains(deletedAt.ToString(), body);
    }

    [Fact]
    public async Task RunAsync_LocalSoftDeletedGoalProgress_DeletedAtSerializedInUploadBody()
    {
        await _accountService.CreateAccountAsync("user75", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var deletedAt = 99_000_003L;
        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalFk = System.Guid.NewGuid().ToString(), NextStepItems = "deleted step",
            UpdatedOn = deletedAt, DeletedAt = deletedAt
        });

        var capturingHandler = new CapturingHandler();
        await BuildSyncService(capturingHandler).RunAsync(account);

        var body = capturingHandler.GetBodyFor("sync/goal-progress");
        Assert.NotNull(body);
        Assert.Contains(deletedAt.ToString(), body);
    }

    [Fact]
    public async Task RunAsync_LocalSoftDeletedTodo_DeletedAtSerializedInUploadBody()
    {
        await _accountService.CreateAccountAsync("user76", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var deletedAt = 99_000_004L;
        await _db.InsertOrReplaceAsync(new Todo
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Title = "deleted todo", UpdatedOn = deletedAt, DeletedAt = deletedAt
        });

        var capturingHandler = new CapturingHandler();
        await BuildSyncService(capturingHandler).RunAsync(account);

        var body = capturingHandler.GetBodyFor("sync/todo");
        Assert.NotNull(body);
        Assert.Contains(deletedAt.ToString(), body);
    }

    [Fact]
    public async Task RunAsync_TwoLocalJournalsModified_BothIncludedInUpload()
    {
        await _accountService.CreateAccountAsync("user82", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid1 = System.Guid.NewGuid().ToString();
        var guid2 = System.Guid.NewGuid().ToString();

        await _db.InsertOrReplaceAsync(new Journal
        {
            Guid = guid1, AccountFk = account.Guid, Notes = "first journal",
            EnteredDate = now, UpdatedOn = now
        });
        await _db.InsertOrReplaceAsync(new Journal
        {
            Guid = guid2, AccountFk = account.Guid, Notes = "second journal",
            EnteredDate = now + 1, UpdatedOn = now + 1
        });

        var capturingHandler = new CapturingHandler();
        var result = await BuildSyncService(capturingHandler).RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var body = capturingHandler.GetBodyFor("sync/journal");
        Assert.NotNull(body);
        Assert.Contains(guid1, body);
        Assert.Contains(guid2, body);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsTwoGoals_BothUpsertedLocally()
    {
        await _accountService.CreateAccountAsync("user83", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var goal1 = new GoalSyncDto(System.Guid.NewGuid().ToString(), account.Guid, "first goal",
            null, null, 1000, null, null, 1000, null);
        var goal2 = new GoalSyncDto(System.Guid.NewGuid().ToString(), account.Guid, "second goal",
            null, null, 2000, null, null, 2000, null);

        var service = BuildSyncService(new MultiGoalSyncHandler(goal1, goal2));
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var goals = await _goalRepo.GetAllActiveAsync(account.Guid);
        Assert.Equal(2, goals.Count);
        Assert.Contains(goals, g => g.GoalText == "first goal");
        Assert.Contains(goals, g => g.GoalText == "second goal");
    }

    [Fact]
    public async Task RunAsync_ServerReturnsTwoTodos_BothUpsertedLocally()
    {
        await _accountService.CreateAccountAsync("user84", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var todo1 = new TodoSyncDto(System.Guid.NewGuid().ToString(), account.Guid, "first task",
            null, null, null, 1000, null);
        var todo2 = new TodoSyncDto(System.Guid.NewGuid().ToString(), account.Guid, "second task",
            null, null, null, 2000, null);

        var service = BuildSyncService(new MultiTodoSyncHandler(todo1, todo2));
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var todos = await _todoRepo.GetPendingAsync(account.Guid);
        Assert.Equal(2, todos.Count);
        Assert.Contains(todos, t => t.Title == "first task");
        Assert.Contains(todos, t => t.Title == "second task");
    }

    [Fact]
    public async Task RunAsync_ServerReturnsTwoGoalProgress_BothUpsertedLocally()
    {
        await _accountService.CreateAccountAsync("user85", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var goalGuid = System.Guid.NewGuid().ToString();
        var progress1 = new GoalProgressSyncDto(System.Guid.NewGuid().ToString(), account.Guid,
            goalGuid, "step one", null, 1000, null);
        var progress2 = new GoalProgressSyncDto(System.Guid.NewGuid().ToString(), account.Guid,
            goalGuid, "step two", null, 2000, null);

        var service = BuildSyncService(new MultiGoalProgressSyncHandler(progress1, progress2));
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var records = await _goalProgressRepo.GetForGoalAsync(goalGuid);
        Assert.Equal(2, records.Count);
        Assert.Contains(records, p => p.NextStepItems == "step one");
        Assert.Contains(records, p => p.NextStepItems == "step two");
    }

    [Fact]
    public async Task RunAsync_TwoLocalTodosModified_BothIncludedInUpload()
    {
        await _accountService.CreateAccountAsync("user86", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid1 = System.Guid.NewGuid().ToString();
        var guid2 = System.Guid.NewGuid().ToString();

        await _db.InsertOrReplaceAsync(new Todo
        {
            Guid = guid1, AccountFk = account.Guid, Title = "first todo",
            UpdatedOn = now
        });
        await _db.InsertOrReplaceAsync(new Todo
        {
            Guid = guid2, AccountFk = account.Guid, Title = "second todo",
            UpdatedOn = now + 1
        });

        var capturingHandler = new CapturingHandler();
        var result = await BuildSyncService(capturingHandler).RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var body = capturingHandler.GetBodyFor("sync/todo");
        Assert.NotNull(body);
        Assert.Contains(guid1, body);
        Assert.Contains(guid2, body);
    }

    [Fact]
    public async Task RunAsync_TwoLocalGoalsModified_BothIncludedInUpload()
    {
        await _accountService.CreateAccountAsync("user87", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid1 = System.Guid.NewGuid().ToString();
        var guid2 = System.Guid.NewGuid().ToString();

        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = guid1, AccountFk = account.Guid, GoalText = "first goal",
            EnteredDate = now, UpdatedOn = now
        });
        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = guid2, AccountFk = account.Guid, GoalText = "second goal",
            EnteredDate = now + 1, UpdatedOn = now + 1
        });

        var capturingHandler = new CapturingHandler();
        var result = await BuildSyncService(capturingHandler).RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var body = capturingHandler.GetBodyFor("sync/goal");
        Assert.NotNull(body);
        Assert.Contains(guid1, body);
        Assert.Contains(guid2, body);
    }

    [Fact]
    public async Task RunAsync_TwoLocalGoalProgressesModified_BothIncludedInUpload()
    {
        await _accountService.CreateAccountAsync("user88", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goalGuid = System.Guid.NewGuid().ToString();
        var guid1 = System.Guid.NewGuid().ToString();
        var guid2 = System.Guid.NewGuid().ToString();

        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = guid1, AccountFk = account.Guid, GoalFk = goalGuid,
            NextStepItems = "step one", UpdatedOn = now
        });
        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = guid2, AccountFk = account.Guid, GoalFk = goalGuid,
            NextStepItems = "step two", UpdatedOn = now + 1
        });

        var capturingHandler = new CapturingHandler();
        var result = await BuildSyncService(capturingHandler).RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var body = capturingHandler.GetBodyFor("sync/goal-progress");
        Assert.NotNull(body);
        Assert.Contains(guid1, body);
        Assert.Contains(guid2, body);
    }

    [Fact]
    public async Task RunAsync_LocalGoalProgress_NullNextStepsMeetingDateOnly_IncludedInUploadRequest()
    {
        await _accountService.CreateAccountAsync("user89", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nextMeeting = updatedOn + 3_000_000L;
        var progressGuid = System.Guid.NewGuid().ToString();
        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = progressGuid, AccountFk = account.Guid,
            GoalFk = System.Guid.NewGuid().ToString(),
            NextStepItems = null,
            NextMeetingDate = nextMeeting, UpdatedOn = updatedOn
        });

        var capturingHandler = new CapturingHandler();
        var service = BuildSyncService(capturingHandler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var body = capturingHandler.GetBodyFor("sync/goal-progress");
        Assert.NotNull(body);
        Assert.Contains(progressGuid, body);
        Assert.Contains(nextMeeting.ToString(), body);
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
// Delays health response past the 5-second CancellationTokenSource timeout in SyncService
public class SlowHealthHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }
}

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

// Captures the Authorization header from the first request; returns 200 for all calls
public class AuthHeaderCapturingHandler : HttpMessageHandler
{
    public System.Net.Http.Headers.AuthenticationHeaderValue? CapturedAuthHeader { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CapturedAuthHeader ??= request.Headers.Authorization;
        if (request.RequestUri!.PathAndQuery.Contains("health"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { status = "ok" })
            });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { Records = Array.Empty<object>() })
        });
    }
}

// Health passes; every entity sync call throws HttpRequestException (both initial and retry fail)
public class AlwaysNetworkErrorEntityHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri!.PathAndQuery.Contains("health"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { status = "ok" })
            });
        throw new HttpRequestException("Simulated persistent network error");
    }
}

// Returns two journal records in the sync response; all other endpoints return empty
public class MultiJournalSyncHandler(JournalSyncDto journal1, JournalSyncDto journal2) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri!.PathAndQuery.Contains("health"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { status = "ok" })
            });
        if (request.RequestUri.PathAndQuery.Contains("sync/journal"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new SyncResponseDto<JournalSyncDto>([journal1, journal2]))
            });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { Records = Array.Empty<object>() })
        });
    }
}

public class MultiGoalSyncHandler(GoalSyncDto goal1, GoalSyncDto goal2) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri!.PathAndQuery.Contains("health"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { status = "ok" })
            });
        if (request.RequestUri.PathAndQuery.Contains("sync/goal") &&
            !request.RequestUri.PathAndQuery.Contains("sync/goal-progress"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new SyncResponseDto<GoalSyncDto>([goal1, goal2]))
            });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { Records = Array.Empty<object>() })
        });
    }
}

public class MultiTodoSyncHandler(TodoSyncDto todo1, TodoSyncDto todo2) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri!.PathAndQuery.Contains("health"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { status = "ok" })
            });
        if (request.RequestUri.PathAndQuery.Contains("sync/todo"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new SyncResponseDto<TodoSyncDto>([todo1, todo2]))
            });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { Records = Array.Empty<object>() })
        });
    }
}

public class MultiGoalProgressSyncHandler(GoalProgressSyncDto p1, GoalProgressSyncDto p2) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri!.PathAndQuery.Contains("health"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { status = "ok" })
            });
        if (request.RequestUri.PathAndQuery.Contains("sync/goal-progress"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new SyncResponseDto<GoalProgressSyncDto>([p1, p2]))
            });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { Records = Array.Empty<object>() })
        });
    }
}

// Health passes; entity sync returns 401 — simulates expired JWT on entity endpoint
public class EntitySync401Handler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri!.PathAndQuery.Contains("health"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { status = "ok" })
            });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
    }
}

// Health passes; entity sync returns 403
public class EntitySync403Handler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri!.PathAndQuery.Contains("health"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { status = "ok" })
            });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
    }
}
