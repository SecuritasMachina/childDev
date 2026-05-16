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
}
