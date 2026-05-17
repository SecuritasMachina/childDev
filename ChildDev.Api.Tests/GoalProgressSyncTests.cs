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
}
