using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChildDev.Api.Models.Dtos;
using ChildDev.Api.Tests.Helpers;

namespace ChildDev.Api.Tests;

public class GoalProgressSyncTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(string jwt, string accountGuid)> RegisterAsync(string nick)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(nick, "pinhash123"));
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        return (auth!.Jwt, auth.AccountGuid);
    }

    [Fact]
    public async Task Sync_NewGoalProgress_StoredAndReturned()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var goalGuid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalGuid, "Step 1 done", null, ts, null)], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();
        Assert.Single(body!.Records);
        Assert.Equal("Step 1 done", body.Records[0].NextStepItems);
    }

    [Fact]
    public async Task Sync_WithoutAuth_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sync_ClientWinsWhenNewerUpdatedOn()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_lww1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var goalGuid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalGuid, "old step", null, ts, null)], 0));
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalGuid, "new step", null, ts + 1000, null)], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();
        Assert.Equal("new step", body!.Records[0].NextStepItems);
    }

    [Fact]
    public async Task Sync_ServerWinsWhenNewerUpdatedOn()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_lww2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var goalGuid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalGuid, "server-wins", null, ts + 2000, null)], 0));
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalGuid, "client-stale", null, ts + 1000, null)], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();
        Assert.Equal("server-wins", body!.Records[0].NextStepItems);
    }

    [Fact]
    public async Task Sync_DeltaFiltering_OnlyReturnsRecordsNewerThanLastSyncAt()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_delta1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var oldTs = 1000L;
        var newTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var oldGuid = Guid.NewGuid().ToString();
        var newGuid = Guid.NewGuid().ToString();
        var goalGuid = Guid.NewGuid().ToString();
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([
                new GoalProgressDto(oldGuid, accountGuid, goalGuid, "old", null, oldTs, null),
                new GoalProgressDto(newGuid, accountGuid, goalGuid, "new", null, newTs, null)
            ], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([], oldTs));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();
        Assert.DoesNotContain(body!.Records, r => r.Guid == oldGuid);
        Assert.Contains(body.Records, r => r.Guid == newGuid);
    }

    [Fact]
    public async Task Sync_RecordWithWrongAccountFk_IsRejected()
    {
        var (jwt1, _) = await RegisterAsync("gpsync_guard1");
        var (jwt2, accountGuid2) = await RegisterAsync("gpsync_guard2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt1);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var intruderGuid = Guid.NewGuid().ToString();
        var goalGuid = Guid.NewGuid().ToString();
        var record = new GoalProgressDto(intruderGuid, accountGuid2, goalGuid, "Intruder step", null, ts, null);
        await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([record], 0));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt2);
        var syncResponse = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await syncResponse.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();
        Assert.DoesNotContain(body!.Records, r => r.Guid == intruderGuid);
    }

    [Fact]
    public async Task Sync_SoftDelete_DeletedAtPropagatedInDelta()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_del1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var goalGuid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalGuid, "Step to delete", null, ts, null)], 0));
        var deletedAt = ts + 1000;
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalGuid, null, null, ts + 1000, deletedAt)], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var result = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();
        var deleted = result!.Records.FirstOrDefault(r => r.Guid == guid);
        Assert.NotNull(deleted);
        Assert.Equal(deletedAt, deleted.DeletedAt);
    }

    [Fact]
    public async Task Sync_EmptyBatch_Returns200_WithEmptyList()
    {
        var (jwt, _) = await RegisterAsync("gpsync-empty1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([], 0));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();
        Assert.Empty(body!.Records);
    }

    [Fact]
    public async Task Sync_DeltaIsolation_OtherUsersRecordsNotReturned()
    {
        var (jwt1, accountGuid1) = await RegisterAsync("gpsync_iso1");
        var (jwt2, _) = await RegisterAsync("gpsync_iso2");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt1);
        var guid = Guid.NewGuid().ToString();
        var goalGuid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid1, goalGuid, "user1 private step", null, ts, null)], 0));

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt2);
        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        Assert.DoesNotContain(body!.Records, r => r.Guid == guid);
    }

    [Fact]
    public async Task Sync_NextMeetingDateRoundTrips()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_optional1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid().ToString();
        var goalFk = Guid.NewGuid().ToString();
        var nextMeeting = ts + 7L * 86_400_000L;

        await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([
            new GoalProgressDto(guid, accountGuid, goalFk, "Step 1", nextMeeting, ts, null)
        ], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();
        var record = body!.Records.First(r => r.Guid == guid);

        Assert.Equal(nextMeeting, record.NextMeetingDate);
    }

    [Fact]
    public async Task Sync_Delta_OrderedByUpdatedOnAscending()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_order1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var goalFk = Guid.NewGuid().ToString();
        var t1 = 1_000_000L;
        var t2 = 2_000_000L;
        var t3 = 3_000_000L;

        await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([
            new GoalProgressDto(Guid.NewGuid().ToString(), accountGuid, goalFk, "at t3", null, t3, null),
            new GoalProgressDto(Guid.NewGuid().ToString(), accountGuid, goalFk, "at t1", null, t1, null),
            new GoalProgressDto(Guid.NewGuid().ToString(), accountGuid, goalFk, "at t2", null, t2, null)
        ], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        Assert.Equal(3, body!.Records.Count);
        Assert.Equal(t1, body.Records[0].UpdatedOn);
        Assert.Equal(t2, body.Records[1].UpdatedOn);
        Assert.Equal(t3, body.Records[2].UpdatedOn);
    }

    [Fact]
    public async Task Sync_BatchMixedLWW_PerRecordWinnerApplied()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_mixed_lww");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var goalFk = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var guidA = Guid.NewGuid().ToString();
        var guidB = Guid.NewGuid().ToString();

        // Establish server state: A at ts+1000, B at ts+2000
        await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([
            new GoalProgressDto(guidA, accountGuid, goalFk, "A server", null, ts + 1000, null),
            new GoalProgressDto(guidB, accountGuid, goalFk, "B server", null, ts + 2000, null)
        ], 0));

        // Client sends A at ts+2000 (newer → client wins) and B at ts+1000 (older → server wins)
        await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([
            new GoalProgressDto(guidA, accountGuid, goalFk, "A client newer", null, ts + 2000, null),
            new GoalProgressDto(guidB, accountGuid, goalFk, "B client stale", null, ts + 1000, null)
        ], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        var recordA = body!.Records.First(r => r.Guid == guidA);
        var recordB = body.Records.First(r => r.Guid == guidB);
        Assert.Equal("A client newer", recordA.NextStepItems);
        Assert.Equal("B server", recordB.NextStepItems);
    }

    [Fact]
    public async Task Sync_LastSyncAt_NegativeValue_ReturnsAllRecords()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_neg_lastsync");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var goalFk = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalFk, "Always returned", null, ts, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], -1));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        Assert.Contains(body!.Records, r => r.Guid == guid);
    }

    [Fact]
    public async Task Sync_TieOnUpdatedOn_ServerVersionWins()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_tie");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var goalFk = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalFk, "Server version", null, ts, null)], 0));

        // Same UpdatedOn, different content — strict > means server keeps its version
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalFk, "Client version", null, ts, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        var stored = body!.Records.Single(r => r.Guid == guid);
        Assert.Equal("Server version", stored.NextStepItems);
    }

    [Fact]
    public async Task Sync_GoalFkNotChangedOnLWWUpdate()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_goalfk");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var originalGoalFk = Guid.NewGuid().ToString();
        var differentGoalFk = Guid.NewGuid().ToString();
        var t1 = 1_000_000L;
        var t2 = 2_000_000L;

        // Store the initial record with originalGoalFk
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, originalGoalFk, "initial", null, t1, null)], 0));

        // Send same Guid with a different GoalFk and newer UpdatedOn — ApplyDto should NOT update GoalFk
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, differentGoalFk, "updated", null, t2, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        var stored = body!.Records.Single(r => r.Guid == guid);
        Assert.Equal(originalGoalFk, stored.GoalFk);
        Assert.Equal("updated", stored.NextStepItems);
    }

    [Fact]
    public async Task Sync_LastSyncAt_LargerThanAllRecords_EmptyDelta()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_future_lastsync");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var goalFk = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(Guid.NewGuid().ToString(), accountGuid, goalFk, "old step", null, ts, null)], 0));

        var futureSync = ts + 10_000L;
        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], futureSync));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        Assert.Empty(body!.Records);
    }

    [Fact]
    public async Task Sync_SoftDeleted_CanBeRestoredByClient_ViaNewerUpdate()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_restore");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var goalFk = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Store a soft-deleted progress record
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalFk, "steps", null, ts, ts)], 0));

        // Client sends same Guid with DeletedAt = null and newer UpdatedOn — LWW restores the record
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalFk, "steps", null, ts + 1000, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        var stored = body!.Records.Single(r => r.Guid == guid);
        Assert.Null(stored.DeletedAt);
    }

    [Fact]
    public async Task Sync_NextMeetingDate_CanBeClearedByClient_ViaNewerUpdate()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_mtg_clear");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var goalFk = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Store progress with NextMeetingDate set
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalFk, "steps", ts + 86400000L, ts, null)], 0));

        // Client sends same Guid with NextMeetingDate = null and newer UpdatedOn — LWW clears the meeting date
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalFk, "steps", null, ts + 1000, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        var stored = body!.Records.Single(r => r.Guid == guid);
        Assert.Null(stored.NextMeetingDate);
    }

    [Fact]
    public async Task Sync_BatchWithMixedAccountFk_ValidRecordStoredInvalidSkipped()
    {
        var (jwt1, accountGuid1) = await RegisterAsync("gpsync_mixed_fk1");
        var (_, accountGuid2) = await RegisterAsync("gpsync_mixed_fk2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt1);

        var validGuid = Guid.NewGuid().ToString();
        var intruderGuid = Guid.NewGuid().ToString();
        var goalFk = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Batch contains one valid record (correct AccountFk) and one intruder (wrong AccountFk)
        await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([
            new GoalProgressDto(validGuid, accountGuid1, goalFk, "my steps", null, ts, null),
            new GoalProgressDto(intruderGuid, accountGuid2, goalFk, "intruder", null, ts, null)
        ], 0));

        // Account1 delta: valid record stored, intruder skipped
        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        Assert.Contains(body!.Records, r => r.Guid == validGuid);
        Assert.DoesNotContain(body.Records, r => r.Guid == intruderGuid);
    }

    [Fact]
    public async Task Sync_SoftDelete_NextStepItemsNullInDelta()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_soft_steps");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var goalGuid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalGuid, "steps to delete", null, ts, null)], 0));

        var deletedAt = ts + 1000;
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalGuid, null, null, ts + 1000, deletedAt)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        var deleted = body!.Records.Single(r => r.Guid == guid);
        Assert.Equal(deletedAt, deleted.DeletedAt);
        Assert.Null(deleted.NextStepItems);
    }

    [Fact]
    public async Task Sync_Delta_AccountFkIncludedInResponse()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_accountfk");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var goalFk = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalFk, "step content", null, ts, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        var record = body!.Records.Single(r => r.Guid == guid);
        Assert.Equal(accountGuid, record.AccountFk);
    }

    [Fact]
    public async Task Sync_SoftDelete_UpdatedOnEqualsDeletedAtInDelta()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_lwwdel1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var goalFk = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalFk, "step", null, ts, null)], 0));
        var deletedAt = ts + 500;
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalFk, null, null, deletedAt, deletedAt)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();
        var deleted = body!.Records.Single(r => r.Guid == guid);

        Assert.NotNull(deleted.DeletedAt);
        Assert.Equal(deleted.DeletedAt!.Value, deleted.UpdatedOn);
    }

    [Fact]
    public async Task Sync_DuplicateGuidsInBatch_Returns422()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_dupguid1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var goalFk = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var dup = new GoalProgressDto(guid, accountGuid, goalFk, "step", null, ts, null);

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([dup, dup], 0));

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_FutureUpdatedOn_Returns422()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_future1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var futureTs = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds();

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(Guid.NewGuid().ToString(), accountGuid, Guid.NewGuid().ToString(), "step", null, futureTs, null)], 0));

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_TooManyRecords_Returns400()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_toomany1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var records = Enumerable.Range(0, 501)
            .Select(_ => new GoalProgressDto(Guid.NewGuid().ToString(), accountGuid, Guid.NewGuid().ToString(), "step", null, ts, null))
            .ToList();

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>(records, 0));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sync_InvalidGoalFkFormat_Returns422()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_badgoalfk1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(Guid.NewGuid().ToString(), accountGuid, "not-a-uuid", "step", null, ts, null)], 0));

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_BlankNextStepItems_Returns422()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_blanksteps1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(Guid.NewGuid().ToString(), accountGuid, Guid.NewGuid().ToString(), "   ", null, ts, null)], 0));

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_SameGuidUploadedTwice_DeltaContainsExactlyOneRecord()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_idempotent1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid().ToString();
        var goalGuid = Guid.NewGuid().ToString();

        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalGuid, "first upload", null, ts, null)], 0));

        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalGuid, "second upload", null, ts + 1, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        var matches = body!.Records.Where(r => r.Guid == guid).ToList();
        Assert.Single(matches);
        Assert.Equal("second upload", matches[0].NextStepItems);
    }

    [Fact]
    public async Task Sync_LastSyncAt_ExactlyEqualToRecordUpdatedOn_ExcludedFromDelta()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_exact_boundary1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid().ToString();
        var goalGuid = Guid.NewGuid().ToString();

        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuid, goalGuid, "boundary step", null, ts, null)], 0));

        // LastSyncAt == record.UpdatedOn — strict > means this record must NOT appear in delta
        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], ts));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        Assert.DoesNotContain(body!.Records, r => r.Guid == guid);
    }

    [Fact]
    public async Task Sync_DeltaDoesNotContainOtherAccountsRecords()
    {
        // Account A uploads a goal-progress entry
        var (jwtA, accountGuidA) = await RegisterAsync("gpsync_isolation_a1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtA);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid().ToString();
        var goalGuid = Guid.NewGuid().ToString();
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(guid, accountGuidA, goalGuid, "account A step", null, ts, null)], 0));

        // Account B fetches delta — must NOT see account A's progress entry
        var (jwtB, _) = await RegisterAsync("gpsync_isolation_b1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtB);
        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        Assert.DoesNotContain(body!.Records, r => r.Guid == guid);
    }

    [Fact]
    public async Task Sync_DeletedAtGreaterThanUpdatedOn_Returns422()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_deletedinvariant1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goalGuid = Guid.NewGuid().ToString();

        // DeletedAt = ts+1, UpdatedOn = ts → invalid invariant
        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(Guid.NewGuid().ToString(), accountGuid, goalGuid, "step", null, ts, ts + 1)], 0));

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_MixedBatch_NewAndExistingBothPersisted()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync_mixedbatch1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goalGuid = Guid.NewGuid().ToString();
        var existingGuid = Guid.NewGuid().ToString();
        var newGuid = Guid.NewGuid().ToString();

        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([new GoalProgressDto(existingGuid, accountGuid, goalGuid, "original step", null, ts, null)], 0));

        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([
                new GoalProgressDto(existingGuid, accountGuid, goalGuid, "updated step", null, ts + 1, null),
                new GoalProgressDto(newGuid, accountGuid, goalGuid, "brand new step", null, ts, null)
            ], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        var existing = body!.Records.FirstOrDefault(r => r.Guid == existingGuid);
        var newRecord = body.Records.FirstOrDefault(r => r.Guid == newGuid);
        Assert.NotNull(existing);
        Assert.Equal("updated step", existing.NextStepItems);
        Assert.NotNull(newRecord);
        Assert.Equal("brand new step", newRecord.NextStepItems);
    }

    [Fact]
    public async Task Sync_OrphanGoalProgress_StoredEvenWhenGoalDoesNotExist()
    {
        // GoalProgress can be uploaded with a GoalFk that has no corresponding Goal.
        // The API must not enforce referential integrity at the sync layer — the Goal
        // may arrive in a later sync, and rejecting orphans would cause data loss.
        var (jwt, accountGuid) = await RegisterAsync("gpsync_orphan");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var progressGuid = Guid.NewGuid().ToString();
        var nonExistentGoalGuid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var upload = await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([
                new GoalProgressDto(progressGuid, accountGuid, nonExistentGoalGuid, "orphan step", null, ts, null)
            ], 0));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress", new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();
        var stored = body!.Records.FirstOrDefault(r => r.Guid == progressGuid);
        Assert.NotNull(stored);
        Assert.Equal(nonExistentGoalGuid, stored.GoalFk);
        Assert.Equal("orphan step", stored.NextStepItems);
    }
}
