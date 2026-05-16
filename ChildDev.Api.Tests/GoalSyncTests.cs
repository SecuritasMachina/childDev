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
}
