using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChildDev.Api.Models.Dtos;
using ChildDev.Api.Tests.Helpers;

namespace ChildDev.Api.Tests;

public class JournalSyncTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(string jwt, string accountGuid)> RegisterAsync(string nick)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(nick, "pinhash123"));
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        return (auth!.Jwt, auth.AccountGuid);
    }

    [Fact]
    public async Task Sync_EmptyBatch_Returns200_WithEmptyList()
    {
        var (jwt, _) = await RegisterAsync("jsync1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var response = await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([], 0));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<JournalDto>>();
        Assert.Empty(body!.Records);
    }

    [Fact]
    public async Task Sync_NewRecord_ServerStoresIt_AndReturnsOnNextSync()
    {
        var (jwt, accountGuid) = await RegisterAsync("jsync2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new JournalDto(guid, accountGuid, "My note", null, null, null, updatedOn, updatedOn, null);
        await _client.PostAsJsonAsync("/api/sync/journal", new SyncRequest<JournalDto>([journal], 0));
        var response2 = await _client.PostAsJsonAsync("/api/sync/journal", new SyncRequest<JournalDto>([], 0));
        var body = await response2.Content.ReadFromJsonAsync<SyncResponse<JournalDto>>();
        Assert.Single(body!.Records);
        Assert.Equal(guid, body.Records[0].Guid);
        Assert.Equal("My note", body.Records[0].Notes);
    }

    [Fact]
    public async Task Sync_ClientWinsWhenNewerUpdatedOn()
    {
        var (jwt, accountGuid) = await RegisterAsync("jsync3");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([new JournalDto(guid, accountGuid, "old", null, null, null, 1000, 1000, null)], 0));
        await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([new JournalDto(guid, accountGuid, "new", null, null, null, 2000, 2000, null)], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/journal", new SyncRequest<JournalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<JournalDto>>();
        Assert.Equal("new", body!.Records[0].Notes);
    }

    [Fact]
    public async Task Sync_ServerWinsWhenNewerUpdatedOn()
    {
        var (jwt, accountGuid) = await RegisterAsync("jsync4");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([new JournalDto(guid, accountGuid, "server-wins", null, null, null, 2000, 2000, null)], 0));
        await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([new JournalDto(guid, accountGuid, "client-stale", null, null, null, 1000, 1000, null)], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/journal", new SyncRequest<JournalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<JournalDto>>();
        Assert.Equal("server-wins", body!.Records[0].Notes);
    }

    [Fact]
    public async Task Sync_WithoutAuth_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.PostAsJsonAsync("/api/sync/journal", new SyncRequest<JournalDto>([], 0));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sync_DeltaFiltering_OnlyReturnsRecordsNewerThanLastSyncAt()
    {
        var (jwt, accountGuid) = await RegisterAsync("jsync_delta1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var oldTs = 1000L;
        var newTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var oldGuid = Guid.NewGuid().ToString();
        var newGuid = Guid.NewGuid().ToString();
        await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([
                new JournalDto(oldGuid, accountGuid, "old note", null, null, null, oldTs, oldTs, null),
                new JournalDto(newGuid, accountGuid, "new note", null, null, null, newTs, newTs, null)
            ], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([], oldTs));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<JournalDto>>();
        Assert.DoesNotContain(body!.Records, r => r.Guid == oldGuid);
        Assert.Contains(body.Records, r => r.Guid == newGuid);
    }

    [Fact]
    public async Task Sync_RecordWithWrongAccountFk_IsRejected()
    {
        var (jwt1, _) = await RegisterAsync("jsync_guard1");
        var (jwt2, accountGuid2) = await RegisterAsync("jsync_guard2");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt1);
        var intruderGuid = Guid.NewGuid().ToString();
        var record = new JournalDto(intruderGuid, accountGuid2, "intruder note", null, null, null, 1000, 1000, null);
        var uploadResponse = await _client.PostAsJsonAsync("/api/sync/journal", new SyncRequest<JournalDto>([record], 0));
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt2);
        var syncResponse = await _client.PostAsJsonAsync("/api/sync/journal", new SyncRequest<JournalDto>([], 0));
        var body = await syncResponse.Content.ReadFromJsonAsync<SyncResponse<JournalDto>>();
        Assert.DoesNotContain(body!.Records, r => r.Guid == intruderGuid);
    }

    [Fact]
    public async Task Sync_SoftDelete_DeletedAtPropagatedInDelta()
    {
        var (jwt, accountGuid) = await RegisterAsync("jsync_del1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([new JournalDto(guid, accountGuid, "to delete", null, null, null, ts, ts, null)], 0));
        var deletedAt = ts + 1000;
        await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([new JournalDto(guid, accountGuid, null, null, null, null, ts, ts + 1000, deletedAt)], 0));
        var response = await _client.PostAsJsonAsync("/api/sync/journal", new SyncRequest<JournalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<JournalDto>>();
        var deleted = body!.Records.FirstOrDefault(r => r.Guid == guid);
        Assert.NotNull(deleted);
        Assert.Equal(deletedAt, deleted.DeletedAt);
    }

    [Fact]
    public async Task Sync_DeltaIsolation_OtherUsersRecordsNotReturned()
    {
        var (jwt1, accountGuid1) = await RegisterAsync("jsync_iso1");
        var (jwt2, _) = await RegisterAsync("jsync_iso2");

        // User 1 uploads a journal entry
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt1);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([new JournalDto(guid, accountGuid1, "user1 private note", null, null, null, ts, ts, null)], 0));

        // User 2 syncs with LastSyncAt=0 (would return everything for their account)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt2);
        var response = await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<JournalDto>>();

        Assert.DoesNotContain(body!.Records, r => r.Guid == guid);
    }

    [Fact]
    public async Task Sync_OptionalFieldsRoundTrip()
    {
        var (jwt, accountGuid) = await RegisterAsync("jsync_optional1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid().ToString();

        await _client.PostAsJsonAsync("/api/sync/journal", new SyncRequest<JournalDto>([
            new JournalDto(guid, accountGuid, "Full entry", "Swimming", "Happy", "fitness,sport", ts, ts, null)
        ], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/journal", new SyncRequest<JournalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<JournalDto>>();
        var record = body!.Records.First(r => r.Guid == guid);

        Assert.Equal("Swimming", record.Activity);
        Assert.Equal("Happy", record.Mood);
        Assert.Equal("fitness,sport", record.Tags);
    }

    [Fact]
    public async Task Sync_Delta_OrderedByUpdatedOnAscending()
    {
        var (jwt, accountGuid) = await RegisterAsync("jsync_order1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var t1 = 1_000_000L;
        var t2 = 2_000_000L;
        var t3 = 3_000_000L;

        // Insert in non-ascending order
        await _client.PostAsJsonAsync("/api/sync/journal", new SyncRequest<JournalDto>([
            new JournalDto(Guid.NewGuid().ToString(), accountGuid, "at t3", null, null, null, t3, t3, null),
            new JournalDto(Guid.NewGuid().ToString(), accountGuid, "at t1", null, null, null, t1, t1, null),
            new JournalDto(Guid.NewGuid().ToString(), accountGuid, "at t2", null, null, null, t2, t2, null)
        ], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/journal", new SyncRequest<JournalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<JournalDto>>();

        Assert.Equal(3, body!.Records.Count);
        Assert.Equal(t1, body.Records[0].UpdatedOn);
        Assert.Equal(t2, body.Records[1].UpdatedOn);
        Assert.Equal(t3, body.Records[2].UpdatedOn);
    }

    [Fact]
    public async Task Sync_BatchMixedLWW_PerRecordWinnerApplied()
    {
        var (jwt, accountGuid) = await RegisterAsync("jsync_mixed_lww");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var guidA = Guid.NewGuid().ToString();
        var guidB = Guid.NewGuid().ToString();

        // Establish server state: A at ts+1000, B at ts+2000
        await _client.PostAsJsonAsync("/api/sync/journal", new SyncRequest<JournalDto>([
            new JournalDto(guidA, accountGuid, "A server", null, null, null, ts, ts + 1000, null),
            new JournalDto(guidB, accountGuid, "B server", null, null, null, ts, ts + 2000, null)
        ], 0));

        // Client sends A at ts+2000 (newer → client wins) and B at ts+1000 (older → server wins)
        await _client.PostAsJsonAsync("/api/sync/journal", new SyncRequest<JournalDto>([
            new JournalDto(guidA, accountGuid, "A client newer", null, null, null, ts, ts + 2000, null),
            new JournalDto(guidB, accountGuid, "B client stale", null, null, null, ts, ts + 1000, null)
        ], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/journal", new SyncRequest<JournalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<JournalDto>>();

        var recordA = body!.Records.First(r => r.Guid == guidA);
        var recordB = body.Records.First(r => r.Guid == guidB);
        Assert.Equal("A client newer", recordA.Notes);
        Assert.Equal("B server", recordB.Notes);
    }

    [Fact]
    public async Task Sync_LastSyncAt_NegativeValue_ReturnsAllRecords()
    {
        var (jwt, accountGuid) = await RegisterAsync("jsync_neg_lastsync");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([new JournalDto(guid, accountGuid, "Always returned", null, null, null, ts, ts, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/journal", new SyncRequest<JournalDto>([], -1));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<JournalDto>>();

        Assert.Contains(body!.Records, r => r.Guid == guid);
    }

    [Fact]
    public async Task Sync_TieOnUpdatedOn_ServerVersionWins()
    {
        var (jwt, accountGuid) = await RegisterAsync("jsync_tie");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([new JournalDto(guid, accountGuid, "Server version", null, null, null, ts, ts, null)], 0));

        // Same UpdatedOn, different content — strict > means server keeps its version
        await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([new JournalDto(guid, accountGuid, "Client version", null, null, null, ts, ts, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/journal", new SyncRequest<JournalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<JournalDto>>();

        var stored = body!.Records.Single(r => r.Guid == guid);
        Assert.Equal("Server version", stored.Notes);
    }
}
