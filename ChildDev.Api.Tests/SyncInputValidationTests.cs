using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ChildDev.Api.Tests.Helpers;

namespace ChildDev.Api.Tests;

public class SyncInputValidationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> RegisterJwtAsync(string nick)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/register",
            new { NickName = nick, PinHash = "pinhash123" });
        var auth = await res.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        return auth!["Jwt"];
    }

    [Theory]
    [InlineData("/api/sync/journal")]
    [InlineData("/api/sync/goal")]
    [InlineData("/api/sync/goal-progress")]
    [InlineData("/api/sync/todo")]
    public async Task Sync_NullRecords_Returns400(string endpoint)
    {
        var jwt = await RegisterJwtAsync($"nullval_{endpoint.Replace("/", "_")}");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var body = new StringContent(
            "{\"Records\":null,\"LastSyncAt\":0}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync(endpoint, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
