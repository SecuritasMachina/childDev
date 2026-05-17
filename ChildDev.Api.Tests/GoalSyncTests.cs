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

    [Fact]
    public async Task Sync_TieOnUpdatedOn_ServerVersionWins()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_tie");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "Server version", null, null, ts, null, null, ts, null)], 0));

        // Same UpdatedOn, different content — strict > means server keeps its version
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "Client version", null, null, ts, null, null, ts, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        var stored = body!.Records.Single(r => r.Guid == guid);
        Assert.Equal("Server version", stored.GoalText);
    }

    [Fact]
    public async Task Sync_LastSyncAt_LargerThanAllRecords_EmptyDelta()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_future_lastsync");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(Guid.NewGuid().ToString(), accountGuid, "old goal", null, null, ts, null, null, ts, null)], 0));

        var futureSync = ts + 10_000L;
        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], futureSync));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        Assert.Empty(body!.Records);
    }

    [Fact]
    public async Task Sync_CompletedGoal_CanBeUncompletedByClient_ViaNewerUpdate()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_uncomplete");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Store a completed goal
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "goal", null, null, ts, null, ts - 1000, ts, null)], 0));

        // Client sends same Guid with CompletionDate = null and newer UpdatedOn — LWW unsets completion
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "goal", null, null, ts, null, null, ts + 1000, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        var stored = body!.Records.Single(r => r.Guid == guid);
        Assert.Null(stored.CompletionDate);
    }

    [Fact]
    public async Task Sync_SoftDeleted_CanBeRestoredByClient_ViaNewerUpdate()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_restore");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Store a soft-deleted goal
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "goal", null, null, ts, null, null, ts, ts)], 0));

        // Client sends same Guid with DeletedAt = null and newer UpdatedOn — LWW restores the record
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "goal", null, null, ts, null, null, ts + 1000, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        var stored = body!.Records.Single(r => r.Guid == guid);
        Assert.Null(stored.DeletedAt);
    }

    [Fact]
    public async Task Sync_ExpirationDate_CanBeClearedByClient_ViaNewerUpdate()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_exp_clear");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Store a goal with ExpirationDate set
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "goal", null, ts + 2592000000L, ts, null, null, ts, null)], 0));

        // Client sends same Guid with ExpirationDate = null and newer UpdatedOn — LWW removes expiration
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "goal", null, null, ts, null, null, ts + 1000, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        var stored = body!.Records.Single(r => r.Guid == guid);
        Assert.Null(stored.ExpirationDate);
    }

    [Fact]
    public async Task Sync_NextMeetingDate_CanBeClearedByClient_ViaNewerUpdate()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_mtg_clear");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Store a goal with NextMeetingDate set
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "goal", ts + 86400000L, null, ts, null, null, ts, null)], 0));

        // Client sends same Guid with NextMeetingDate = null and newer UpdatedOn — LWW clears the meeting date
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "goal", null, null, ts, null, null, ts + 1000, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        var stored = body!.Records.Single(r => r.Guid == guid);
        Assert.Null(stored.NextMeetingDate);
    }

    [Fact]
    public async Task Sync_EnteredDate_NotUpdatedOnLWWOverwrite()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_entered_date");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var originalEnteredDate = 1_000_000L;
        var t1 = 1_000_000L;
        var t2 = 2_000_000L;

        // Store goal with EnteredDate = originalEnteredDate, UpdatedOn = t1
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "goal text", null, null, originalEnteredDate, null, null, t1, null)], 0));

        // Client sends same Guid with a different EnteredDate and newer UpdatedOn — ApplyDto must not update EnteredDate
        var differentEnteredDate = 9_999_999L;
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "goal text", null, null, differentEnteredDate, null, null, t2, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        var stored = body!.Records.Single(r => r.Guid == guid);
        Assert.Equal(originalEnteredDate, stored.EnteredDate);
    }

    [Fact]
    public async Task Sync_MeasurableOutcome_CanBeClearedByClient_ViaNewerUpdate()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_mo_clear");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Store goal with MeasurableOutcome set
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "goal", null, null, ts, "Run 5km", null, ts, null)], 0));

        // Client sends newer UpdatedOn with MeasurableOutcome = null — LWW must clear it
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "goal", null, null, ts, null, null, ts + 1000, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        var stored = body!.Records.Single(r => r.Guid == guid);
        Assert.Null(stored.MeasurableOutcome);
    }

    [Fact]
    public async Task Sync_BatchWithMixedAccountFk_ValidRecordStoredInvalidSkipped()
    {
        var (jwt1, accountGuid1) = await RegisterAsync("gsync_mixed_fk1");
        var (_, accountGuid2) = await RegisterAsync("gsync_mixed_fk2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt1);

        var validGuid = Guid.NewGuid().ToString();
        var intruderGuid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Batch contains one valid record (correct AccountFk) and one intruder (wrong AccountFk)
        await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([
            new GoalDto(validGuid, accountGuid1, "my goal", null, null, ts, null, null, ts, null),
            new GoalDto(intruderGuid, accountGuid2, "intruder", null, null, ts, null, null, ts, null)
        ], 0));

        // Account1 delta: valid record stored, intruder skipped
        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        Assert.Contains(body!.Records, r => r.Guid == validGuid);
        Assert.DoesNotContain(body.Records, r => r.Guid == intruderGuid);
    }

    [Fact]
    public async Task Sync_SoftDelete_GoalTextNullInDelta()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_soft_text");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "goal to delete", null, null, ts, null, null, ts, null)], 0));

        var deletedAt = ts + 1000;
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, null, null, null, ts, null, null, ts + 1000, deletedAt)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        var deleted = body!.Records.Single(r => r.Guid == guid);
        Assert.Equal(deletedAt, deleted.DeletedAt);
        Assert.Null(deleted.GoalText);
    }

    [Fact]
    public async Task Sync_Delta_AccountFkIncludedInResponse()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_accountfk");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "my goal", null, null, ts, null, null, ts, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        var record = body!.Records.Single(r => r.Guid == guid);
        Assert.Equal(accountGuid, record.AccountFk);
    }

    [Fact]
    public async Task Sync_SoftDelete_UpdatedOnEqualsDeletedAtInDelta()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_lwwdel1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "my goal", null, null, ts, null, null, ts, null)], 0));
        var deletedAt = ts + 500;
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, null, null, null, ts, null, null, deletedAt, deletedAt)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();
        var deleted = body!.Records.Single(r => r.Guid == guid);

        Assert.NotNull(deleted.DeletedAt);
        Assert.Equal(deleted.DeletedAt!.Value, deleted.UpdatedOn);
    }

    [Fact]
    public async Task Sync_DuplicateGuidsInBatch_Returns422()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_dupguid1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var dup = new GoalDto(guid, accountGuid, "goal text", null, null, ts, null, null, ts, null);

        var response = await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([dup, dup], 0));

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_FutureUpdatedOn_Returns422()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_future1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var futureTs = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds();

        var response = await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(Guid.NewGuid().ToString(), accountGuid, "goal text", null, null, ts, null, null, futureTs, null)], 0));

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_TooManyRecords_Returns400()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_toomany1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var records = Enumerable.Range(0, 501)
            .Select(_ => new GoalDto(Guid.NewGuid().ToString(), accountGuid, "goal text", null, null, ts, null, null, ts, null))
            .ToList();

        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>(records, 0));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sync_BlankGoalText_Returns422()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_blankgoal1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var response = await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(Guid.NewGuid().ToString(), accountGuid, "   ", null, null, ts, null, null, ts, null)], 0));

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_FutureExpirationDate_Returns422()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_futureexp1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var farFuture = DateTimeOffset.UtcNow.AddYears(15).ToUnixTimeMilliseconds();

        var response = await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(Guid.NewGuid().ToString(), accountGuid, "goal text", null, farFuture, ts, null, null, ts, null)], 0));

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_LastSyncAt_ExactlyEqualToRecordUpdatedOn_ExcludedFromDelta()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_exact_boundary1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid().ToString();

        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "boundary goal", null, null, ts, null, null, ts, null)], 0));

        // LastSyncAt == record.UpdatedOn — strict > means this record must NOT appear in delta
        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], ts));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        Assert.DoesNotContain(body!.Records, r => r.Guid == guid);
    }

    [Fact]
    public async Task Sync_SameGuidUploadedTwice_DeltaContainsExactlyOneRecord()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_idempotent1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid().ToString();

        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "first upload", null, null, ts, null, null, ts, null)], 0));

        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, "second upload", null, null, ts, null, null, ts + 1, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        var matches = body!.Records.Where(r => r.Guid == guid).ToList();
        Assert.Single(matches);
        Assert.Equal("second upload", matches[0].GoalText);
    }

    [Fact]
    public async Task Sync_DeltaDoesNotContainOtherAccountsRecords()
    {
        // Account A uploads a goal
        var (jwtA, accountGuidA) = await RegisterAsync("gsync_isolation_a1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtA);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid().ToString();
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuidA, "account A goal", null, null, ts, null, null, ts, null)], 0));

        // Account B fetches delta — must NOT see account A's record
        var (jwtB, _) = await RegisterAsync("gsync_isolation_b1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtB);
        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        Assert.DoesNotContain(body!.Records, r => r.Guid == guid);
    }

    [Fact]
    public async Task Sync_DeletedAtGreaterThanUpdatedOn_Returns422()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_deletedinvariant1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var response = await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(Guid.NewGuid().ToString(), accountGuid, "goal text", null, null, ts, null, null, ts, ts + 1)], 0));

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_MixedBatch_NewAndExistingBothPersisted()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync_mixedbatch1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var existingGuid = Guid.NewGuid().ToString();
        var newGuid = Guid.NewGuid().ToString();

        // Upload the existing record first
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(existingGuid, accountGuid, "original text", null, null, ts, null, null, ts, null)], 0));

        // Now upload a batch with an update to the existing + a new record
        await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([
                new GoalDto(existingGuid, accountGuid, "updated text", null, null, ts, null, null, ts + 1, null),
                new GoalDto(newGuid, accountGuid, "brand new goal", null, null, ts, null, null, ts, null)
            ], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        var existing = body!.Records.FirstOrDefault(r => r.Guid == existingGuid);
        var newRecord = body.Records.FirstOrDefault(r => r.Guid == newGuid);
        Assert.NotNull(existing);
        Assert.Equal("updated text", existing.GoalText);
        Assert.NotNull(newRecord);
        Assert.Equal("brand new goal", newRecord.GoalText);
    }

    [Fact]
    public async Task Sync_SoftDeletedRecord_BlankGoalText_Accepted()
    {
        // Validation requires GoalText only for non-deleted records (DeletedAt is null).
        // A soft-deleted goal with null GoalText must be accepted and stored.
        var (jwt, accountGuid) = await RegisterAsync("gsync_softdel_blank");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var upload = await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, null, null, null, ts, null, null, ts, ts)], 0));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();
        var stored = body!.Records.FirstOrDefault(r => r.Guid == guid);
        Assert.NotNull(stored);
        Assert.Equal(ts, stored!.DeletedAt);
    }

    [Fact]
    public async Task Sync_CompletedGoal_BlankGoalText_Accepted()
    {
        // Validation exempts completed goals (CompletionDate set) from the blank-GoalText check,
        // matching the same exemption for soft-deleted records.
        var (jwt, accountGuid) = await RegisterAsync("gsync_completed_blank");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var upload = await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([new GoalDto(guid, accountGuid, null, null, null, ts, null, ts, ts, null)], 0));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        var response = await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();
        var stored = body!.Records.FirstOrDefault(r => r.Guid == guid);
        Assert.NotNull(stored);
        Assert.Equal(ts, stored!.CompletionDate);
    }
}
