using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChildDev.Api.Models.Dtos;
using ChildDev.Api.Tests.Helpers;

namespace ChildDev.Api.Tests;

public class TodoSyncTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(string jwt, string accountGuid)> RegisterAsync(string nick)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(nick, "pinhash123"));
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        return (auth!.Jwt, auth.AccountGuid);
    }

    [Fact]
    public async Task Sync_NewTodo_StoredAndReturned()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "Buy groceries", null, null, null, ts, null)], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();
        Assert.Single(body!.Records);
        Assert.Equal("Buy groceries", body.Records[0].Title);
    }

    [Fact]
    public async Task Sync_CompletedTodo_CompletedAtSet_ReturnedInDelta()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "Task", null, null, ts, ts, null)], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();
        Assert.Equal(ts, body!.Records[0].CompletedAt);
    }

    [Fact]
    public async Task Sync_WithoutAuth_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sync_ClientWinsWhenNewerUpdatedOn()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_lww1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "old todo", null, null, null, ts, null)], 0));
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "new todo", null, null, null, ts + 1000, null)], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();
        Assert.Equal("new todo", body!.Records[0].Title);
    }

    [Fact]
    public async Task Sync_ServerWinsWhenNewerUpdatedOn()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_lww2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "server-wins", null, null, null, ts + 2000, null)], 0));
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "client-stale", null, null, null, ts + 1000, null)], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();
        Assert.Equal("server-wins", body!.Records[0].Title);
    }

    [Fact]
    public async Task Sync_DeltaFiltering_OnlyReturnsRecordsNewerThanLastSyncAt()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_delta1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var oldTs = 1000L;
        var newTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var oldGuid = Guid.NewGuid().ToString();
        var newGuid = Guid.NewGuid().ToString();
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([
                new TodoDto(oldGuid, accountGuid, "old todo", null, null, null, oldTs, null),
                new TodoDto(newGuid, accountGuid, "new todo", null, null, null, newTs, null)
            ], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([], oldTs));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();
        Assert.DoesNotContain(body!.Records, r => r.Guid == oldGuid);
        Assert.Contains(body.Records, r => r.Guid == newGuid);
    }

    [Fact]
    public async Task Sync_RecordWithWrongAccountFk_IsRejected()
    {
        var (jwt1, _) = await RegisterAsync("tsync_guard1");
        var (jwt2, accountGuid2) = await RegisterAsync("tsync_guard2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt1);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var intruderGuid = Guid.NewGuid().ToString();
        var record = new TodoDto(intruderGuid, accountGuid2, "Intruder todo", null, null, null, ts, null);
        await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([record], 0));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt2);
        var syncResponse = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await syncResponse.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();
        Assert.DoesNotContain(body!.Records, r => r.Guid == intruderGuid);
    }

    [Fact]
    public async Task Sync_SoftDelete_DeletedAtPropagatedInDelta()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_del1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "to delete", null, null, null, ts, null)], 0));
        var deletedAt = ts + 1000;
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, null, null, null, null, ts + 1000, deletedAt)], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var result = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();
        var deleted = result!.Records.FirstOrDefault(r => r.Guid == guid);
        Assert.NotNull(deleted);
        Assert.Equal(deletedAt, deleted.DeletedAt);
    }

    [Fact]
    public async Task Sync_EmptyBatch_Returns200_WithEmptyList()
    {
        var (jwt, _) = await RegisterAsync("tsync-empty1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var response = await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([], 0));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();
        Assert.Empty(body!.Records);
    }

    [Fact]
    public async Task Sync_DeltaIsolation_OtherUsersRecordsNotReturned()
    {
        var (jwt1, accountGuid1) = await RegisterAsync("tsync_iso1");
        var (jwt2, _) = await RegisterAsync("tsync_iso2");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt1);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid1, "user1 private todo", null, null, null, ts, null)], 0));

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt2);
        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();

        Assert.DoesNotContain(body!.Records, r => r.Guid == guid);
    }

    [Fact]
    public async Task Sync_BatchMixedLWW_PerRecordWinnerApplied()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_mixed_lww");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var guidA = Guid.NewGuid().ToString();
        var guidB = Guid.NewGuid().ToString();

        // Establish server state: A at ts+1000, B at ts+2000
        await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([
            new TodoDto(guidA, accountGuid, "A server", null, null, null, ts + 1000, null),
            new TodoDto(guidB, accountGuid, "B server", null, null, null, ts + 2000, null)
        ], 0));

        // Client sends A at ts+2000 (newer → client wins) and B at ts+1000 (older → server wins)
        await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([
            new TodoDto(guidA, accountGuid, "A client newer", null, null, null, ts + 2000, null),
            new TodoDto(guidB, accountGuid, "B client stale", null, null, null, ts + 1000, null)
        ], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();

        var recordA = body!.Records.First(r => r.Guid == guidA);
        var recordB = body.Records.First(r => r.Guid == guidB);
        Assert.Equal("A client newer", recordA.Title);
        Assert.Equal("B server", recordB.Title);
    }

    [Fact]
    public async Task Sync_LastSyncAt_NegativeValue_ReturnsAllRecords()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_neg_lastsync");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "Always returned", null, null, null, ts, null)], 0));

        // LastSyncAt = -1 means "never synced before". All records must be in delta.
        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], -1));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();

        Assert.Contains(body!.Records, r => r.Guid == guid);
    }

    [Fact]
    public async Task Sync_OptionalFieldsRoundTrip()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_optional1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid().ToString();
        var dueDate = ts + 86_400_000L;

        await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([
            new TodoDto(guid, accountGuid, "Buy groceries", "Pick up milk and eggs", dueDate, null, ts, null)
        ], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();
        var record = body!.Records.First(r => r.Guid == guid);

        Assert.Equal("Pick up milk and eggs", record.Notes);
        Assert.Equal(dueDate, record.DueDate);
    }

    [Fact]
    public async Task Sync_Delta_OrderedByUpdatedOnAscending()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_order1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var t1 = 1_000_000L;
        var t2 = 2_000_000L;
        var t3 = 3_000_000L;

        await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([
            new TodoDto(Guid.NewGuid().ToString(), accountGuid, "at t3", null, null, null, t3, null),
            new TodoDto(Guid.NewGuid().ToString(), accountGuid, "at t1", null, null, null, t1, null),
            new TodoDto(Guid.NewGuid().ToString(), accountGuid, "at t2", null, null, null, t2, null)
        ], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();

        Assert.Equal(3, body!.Records.Count);
        Assert.Equal(t1, body.Records[0].UpdatedOn);
        Assert.Equal(t2, body.Records[1].UpdatedOn);
        Assert.Equal(t3, body.Records[2].UpdatedOn);
    }

    [Fact]
    public async Task Sync_TieOnUpdatedOn_ServerVersionWins()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_tie");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "Server version", null, null, null, ts, null)], 0));

        // Same UpdatedOn, different content — strict > means server keeps its version
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "Client version", null, null, null, ts, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();

        var stored = body!.Records.Single(r => r.Guid == guid);
        Assert.Equal("Server version", stored.Title);
    }

    [Fact]
    public async Task Sync_CompletedTodo_CanBeUncompletedByClient_ViaNewerUpdate()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_uncomplete");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Store a completed todo
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "Task", null, null, ts - 1000, ts, null)], 0));

        // Client sends same Guid with CompletedAt = null and newer UpdatedOn — LWW unsets completion
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "Task", null, null, null, ts + 1000, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();

        var stored = body!.Records.Single(r => r.Guid == guid);
        Assert.Null(stored.CompletedAt);
    }

    [Fact]
    public async Task Sync_LastSyncAt_LargerThanAllRecords_EmptyDelta()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_future_lastsync");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(Guid.NewGuid().ToString(), accountGuid, "old todo", null, null, null, ts, null)], 0));

        var futureSync = ts + 10_000L;
        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], futureSync));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();

        Assert.Empty(body!.Records);
    }

    [Fact]
    public async Task Sync_SoftDeleted_CanBeRestoredByClient_ViaNewerUpdate()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_restore");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Store a soft-deleted todo
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "Task", null, null, null, ts, ts)], 0));

        // Client sends same Guid with DeletedAt = null and newer UpdatedOn — LWW restores the record
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "Task", null, null, null, ts + 1000, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();

        var stored = body!.Records.Single(r => r.Guid == guid);
        Assert.Null(stored.DeletedAt);
    }

    [Fact]
    public async Task Sync_BatchWithMixedAccountFk_ValidRecordStoredInvalidSkipped()
    {
        var (jwt1, accountGuid1) = await RegisterAsync("tsync_mixed_fk1");
        var (_, accountGuid2) = await RegisterAsync("tsync_mixed_fk2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt1);

        var validGuid = Guid.NewGuid().ToString();
        var intruderGuid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Batch contains one valid record (correct AccountFk) and one intruder (wrong AccountFk)
        await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([
            new TodoDto(validGuid, accountGuid1, "my todo", null, null, null, ts, null),
            new TodoDto(intruderGuid, accountGuid2, "intruder", null, null, null, ts, null)
        ], 0));

        // Account1 delta: valid record stored, intruder skipped
        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();

        Assert.Contains(body!.Records, r => r.Guid == validGuid);
        Assert.DoesNotContain(body.Records, r => r.Guid == intruderGuid);
    }

    [Fact]
    public async Task Sync_Notes_CanBeClearedByClient_ViaNewerUpdate()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_notes_clear");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Store todo with Notes set
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "Task", "Some detail", null, null, ts, null)], 0));

        // Client sends newer UpdatedOn with Notes = null — LWW must clear it
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "Task", null, null, null, ts + 1000, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();

        var stored = body!.Records.Single(r => r.Guid == guid);
        Assert.Null(stored.Notes);
    }

    [Fact]
    public async Task Sync_DueDate_CanBeClearedByClient_ViaNewerUpdate()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_due_clear");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Store a todo with DueDate set
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "Task", null, ts + 86400000L, null, ts, null)], 0));

        // Client sends same Guid with DueDate = null and newer UpdatedOn — LWW removes the due date
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "Task", null, null, null, ts + 1000, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();

        var stored = body!.Records.Single(r => r.Guid == guid);
        Assert.Null(stored.DueDate);
    }

    [Fact]
    public async Task Sync_SoftDelete_TitleNullInDelta()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_soft_title");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "todo to delete", null, null, null, ts, null)], 0));

        var deletedAt = ts + 1000;
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, null, null, null, null, ts + 1000, deletedAt)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();

        var deleted = body!.Records.Single(r => r.Guid == guid);
        Assert.Equal(deletedAt, deleted.DeletedAt);
        Assert.Null(deleted.Title);
    }

    [Fact]
    public async Task Sync_Delta_AccountFkIncludedInResponse()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_accountfk");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "my task", null, null, null, ts, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();

        var record = body!.Records.Single(r => r.Guid == guid);
        Assert.Equal(accountGuid, record.AccountFk);
    }

    [Fact]
    public async Task Sync_SoftDelete_UpdatedOnEqualsDeletedAtInDelta()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_lwwdel1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "my task", null, null, null, ts, null)], 0));
        var deletedAt = ts + 500;
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, null, null, null, null, deletedAt, deletedAt)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();
        var deleted = body!.Records.Single(r => r.Guid == guid);

        Assert.NotNull(deleted.DeletedAt);
        Assert.Equal(deleted.DeletedAt!.Value, deleted.UpdatedOn);
    }

    [Fact]
    public async Task Sync_DuplicateGuidsInBatch_Returns422()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_dupguid1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var dup = new TodoDto(guid, accountGuid, "task", null, null, null, ts, null);

        var response = await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([dup, dup], 0));

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_FutureUpdatedOn_Returns422()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_future1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var futureTs = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds();

        var response = await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(Guid.NewGuid().ToString(), accountGuid, "task", null, null, null, futureTs, null)], 0));

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_TooManyRecords_Returns400()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_toomany1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var records = Enumerable.Range(0, 501)
            .Select(_ => new TodoDto(Guid.NewGuid().ToString(), accountGuid, "task", null, null, null, ts, null))
            .ToList();

        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>(records, 0));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sync_BlankTitle_Returns422()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_blanktitle1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var response = await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(Guid.NewGuid().ToString(), accountGuid, "   ", null, null, null, ts, null)], 0));

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_FutureDueDate_Returns422()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_futuredue1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var farFutureDate = DateTimeOffset.UtcNow.AddYears(15).ToUnixTimeMilliseconds();

        var response = await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(Guid.NewGuid().ToString(), accountGuid, "task", null, farFutureDate, null, ts, null)], 0));

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_SameGuidUploadedTwice_DeltaContainsExactlyOneRecord()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_idempotent1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid().ToString();

        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "first upload", null, null, null, ts, null)], 0));

        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "second upload", null, null, null, ts + 1, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();

        var matches = body!.Records.Where(r => r.Guid == guid).ToList();
        Assert.Single(matches);
        Assert.Equal("second upload", matches[0].Title);
    }

    [Fact]
    public async Task Sync_LastSyncAt_ExactlyEqualToRecordUpdatedOn_ExcludedFromDelta()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_exact_boundary1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid().ToString();

        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuid, "boundary task", null, null, null, ts, null)], 0));

        // LastSyncAt == record.UpdatedOn — strict > means this record must NOT appear in delta
        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], ts));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();

        Assert.DoesNotContain(body!.Records, r => r.Guid == guid);
    }

    [Fact]
    public async Task Sync_DeltaDoesNotContainOtherAccountsRecords()
    {
        // Account A uploads a todo
        var (jwtA, accountGuidA) = await RegisterAsync("tsync_isolation_a1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtA);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid().ToString();
        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(guid, accountGuidA, "account A task", null, null, null, ts, null)], 0));

        // Account B fetches delta — must NOT see account A's todo
        var (jwtB, _) = await RegisterAsync("tsync_isolation_b1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtB);
        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();

        Assert.DoesNotContain(body!.Records, r => r.Guid == guid);
    }

    [Fact]
    public async Task Sync_DeletedAtGreaterThanUpdatedOn_Returns422()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_deletedinvariant1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // DeletedAt = ts+1, UpdatedOn = ts → invalid invariant
        var response = await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(Guid.NewGuid().ToString(), accountGuid, "task", null, null, null, ts, ts + 1)], 0));

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_MixedBatch_NewAndExistingBothPersisted()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync_mixedbatch1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var existingGuid = Guid.NewGuid().ToString();
        var newGuid = Guid.NewGuid().ToString();

        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([new TodoDto(existingGuid, accountGuid, "original task", null, null, null, ts, null)], 0));

        await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([
                new TodoDto(existingGuid, accountGuid, "updated task", null, null, null, ts + 1, null),
                new TodoDto(newGuid, accountGuid, "brand new task", null, null, null, ts, null)
            ], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();

        var existing = body!.Records.FirstOrDefault(r => r.Guid == existingGuid);
        var newRecord = body.Records.FirstOrDefault(r => r.Guid == newGuid);
        Assert.NotNull(existing);
        Assert.Equal("updated task", existing.Title);
        Assert.NotNull(newRecord);
        Assert.Equal("brand new task", newRecord.Title);
    }
}
