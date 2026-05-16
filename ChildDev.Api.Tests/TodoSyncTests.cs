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
}
