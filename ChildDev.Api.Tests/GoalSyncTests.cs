using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChildDev.Api.Models.Dtos;
using ChildDev.Api.Tests.Helpers;

namespace ChildDev.Api.Tests;

public class GoalSyncTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(string jwt, string accountGuid)> RegisterAsync(string nick)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(nick, "pinhash123"));
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        return (auth!.Jwt, auth.AccountGuid);
    }

    [Fact]
    public async Task Sync_NewGoal_StoredAndReturned()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "Learn to read", null, null, ts, null, null, ts, null)], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();
        Assert.Single(body!.Records);
        Assert.Equal("Learn to read", body.Records[0].GoalText);
    }

    [Fact]
    public async Task Sync_WithoutAuth_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sync_ClientWinsWhenNewerUpdatedOn()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_lww1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "old goal", null, null, ts, null, null, ts, null)], 0));
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "new goal", null, null, ts + 1000, null, null, ts + 1000, null)], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();
        Assert.Equal("new goal", body!.Records[0].GoalText);
    }

    [Fact]
    public async Task Sync_ServerWinsWhenNewerUpdatedOn()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_lww2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "server-wins", null, null, ts, null, null, ts + 2000, null)], 0));
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "client-stale", null, null, ts, null, null, ts + 1000, null)], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();
        Assert.Equal("server-wins", body!.Records[0].GoalText);
    }

    [Fact]
    public async Task Sync_DeltaFiltering_OnlyReturnsRecordsNewerThanLastSyncAt()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_delta1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var oldTs = 1000L;
        var newTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var oldGuid = Guid.NewGuid().ToString();
        var newGuid = Guid.NewGuid().ToString();
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([
                new GoalDto(oldGuid, accountGuid, "old record", null, null, oldTs, null, null, oldTs, null),
                new GoalDto(newGuid, accountGuid, "new record", null, null, newTs, null, null, newTs, null)
            ], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([], oldTs));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();
        Assert.DoesNotContain(body!.Records, r => r.Guid == oldGuid);
        Assert.Contains(body.Records, r => r.Guid == newGuid);
    }

    [Fact]
    public async Task Sync_RecordWithWrongAccountFk_IsRejected()
    {
        var (jwt1, _) = await RegisterAsync("gsync_guard1");
        var (jwt2, accountGuid2) = await RegisterAsync("gsync_guard2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt1);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var intruderGuid = Guid.NewGuid().ToString();
        var record = new GoalDto(intruderGuid, accountGuid2, "Intruder goal", null, null, ts, null, null, ts, null);
        await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([record], 0));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt2);
        var syncResponse = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await syncResponse.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();
        Assert.DoesNotContain(body!.Records, r => r.Guid == intruderGuid);
    }

    [Fact]
    public async Task Sync_SoftDelete_DeletedAtPropagatedInDelta()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_del1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "to delete", null, null, ts, null, null, ts, null)], 0));
        var deletedAt = ts + 1000;
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, null, null, null, ts, null, null, ts + 1000, deletedAt)], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var result = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();
        var deleted = result!.Records.FirstOrDefault(r => r.Guid == guid);
        Assert.NotNull(deleted);
        Assert.Equal(deletedAt, deleted.DeletedAt);
    }

    [Fact]
    public async Task Sync_EmptyBatch_Returns200_WithEmptyList()
    {
        var (jwt, _) = await RegisterAsync("gsync-empty1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var response = await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([], 0));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();
        Assert.Empty(body!.Records);
    }

    [Fact]
    public async Task Sync_DeltaIsolation_OtherUsersRecordsNotReturned()
    {
        var (jwt1, accountGuid1) = await RegisterAsync("gsync_iso1");
        var (jwt2, _) = await RegisterAsync("gsync_iso2");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt1);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid1, "user1 private goal", null, null, ts, null, null, ts, null)], 0));

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt2);
        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        Assert.DoesNotContain(body!.Records, r => r.Guid == guid);
    }

    [Fact]
    public async Task Sync_OptionalFieldsRoundTrip()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_optional1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid().ToString();
        var nextMeeting = ts + 86_400_000L;
        var expiration = ts + 30L * 86_400_000L;

        await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([
            new GoalDto(guid, accountGuid, "Full goal", nextMeeting, expiration, ts, "Measure outcome", null, ts, null)
        ], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();
        var record = body!.Records.First(r => r.Guid == guid);

        Assert.Equal("Measure outcome", record.MeasurableOutcome);
        Assert.Equal(nextMeeting, record.NextMeetingDate);
        Assert.Equal(expiration, record.ExpirationDate);
    }

    [Fact]
    public async Task Sync_CompletedGoal_CompletionDatePropagatedInDelta()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_complete1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var completionDate = ts - 1000;

        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "Completed goal", null, null, ts, null, completionDate, ts, null)], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        var returned = body!.Records.FirstOrDefault(r => r.Guid == guid);
        Assert.NotNull(returned);
        Assert.Equal(completionDate, returned!.CompletionDate);
    }

    [Fact]
    public async Task Sync_Delta_OrderedByUpdatedOnAscending()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_order1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var t1 = 1_000_000L;
        var t2 = 2_000_000L;
        var t3 = 3_000_000L;

        await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([
            new GoalDto(Guid.NewGuid().ToString(), accountGuid, "at t3", null, null, t3, null, null, t3, null),
            new GoalDto(Guid.NewGuid().ToString(), accountGuid, "at t1", null, null, t1, null, null, t1, null),
            new GoalDto(Guid.NewGuid().ToString(), accountGuid, "at t2", null, null, t2, null, null, t2, null)
        ], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        Assert.Equal(3, body!.Records.Count);
        Assert.Equal(t1, body.Records[0].UpdatedOn);
        Assert.Equal(t2, body.Records[1].UpdatedOn);
        Assert.Equal(t3, body.Records[2].UpdatedOn);
    }

    [Fact]
    public async Task Sync_BatchMixedLWW_PerRecordWinnerApplied()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_mixed_lww");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var guidA = Guid.NewGuid().ToString();
        var guidB = Guid.NewGuid().ToString();

        // Establish server state: A at ts+1000, B at ts+2000
        await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([
            new GoalDto(guidA, accountGuid, "A server", null, null, ts, null, null, ts + 1000, null),
            new GoalDto(guidB, accountGuid, "B server", null, null, ts, null, null, ts + 2000, null)
        ], 0));

        // Client sends A at ts+2000 (newer → client wins) and B at ts+1000 (older → server wins)
        await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([
            new GoalDto(guidA, accountGuid, "A client newer", null, null, ts, null, null, ts + 2000, null),
            new GoalDto(guidB, accountGuid, "B client stale", null, null, ts, null, null, ts + 1000, null)
        ], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        var recordA = body!.Records.First(r => r.Guid == guidA);
        var recordB = body.Records.First(r => r.Guid == guidB);
        Assert.Equal("A client newer", recordA.GoalText);
        Assert.Equal("B server", recordB.GoalText);
    }

    [Fact]
    public async Task Sync_LastSyncAt_NegativeValue_ReturnsAllRecords()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_neg_lastsync");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "Always returned", null, null, ts, null, null, ts, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], -1));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        Assert.Contains(body!.Records, r => r.Guid == guid);
    }
}
