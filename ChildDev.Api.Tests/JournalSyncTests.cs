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
}
