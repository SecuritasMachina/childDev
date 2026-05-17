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

    [Theory]
    [InlineData("/api/sync/journal",  "{\"Records\":[{\"Guid\":\"g1\",\"AccountFk\":\"a1\",\"UpdatedOn\":9999999999999,\"EnteredDate\":0,\"DeletedAt\":null}],\"LastSyncAt\":0}")]
    [InlineData("/api/sync/goal",     "{\"Records\":[{\"Guid\":\"g1\",\"AccountFk\":\"a1\",\"UpdatedOn\":9999999999999,\"EnteredDate\":0,\"DeletedAt\":null}],\"LastSyncAt\":0}")]
    [InlineData("/api/sync/goal-progress", "{\"Records\":[{\"Guid\":\"g1\",\"AccountFk\":\"a1\",\"GoalFk\":\"f1\",\"UpdatedOn\":9999999999999,\"DeletedAt\":null}],\"LastSyncAt\":0}")]
    [InlineData("/api/sync/todo",     "{\"Records\":[{\"Guid\":\"g1\",\"AccountFk\":\"a1\",\"UpdatedOn\":9999999999999,\"DeletedAt\":null}],\"LastSyncAt\":0}")]
    public async Task Sync_FutureUpdatedOn_Returns422(string endpoint, string body)
    {
        var jwt = await RegisterJwtAsync($"futval_{endpoint.Replace("/", "_")}");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.PostAsync(endpoint,
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/sync/journal")]
    [InlineData("/api/sync/goal")]
    [InlineData("/api/sync/goal-progress")]
    [InlineData("/api/sync/todo")]
    public async Task Sync_TooManyRecords_Returns400(string endpoint)
    {
        var jwt = await RegisterJwtAsync($"maxrec_{endpoint.Replace("/", "_")}");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        // Build a JSON body with 501 minimal records
        var records = string.Join(",", Enumerable.Range(0, 501)
            .Select(i => $"{{\"Guid\":\"{i:D36}\",\"AccountFk\":\"a\",\"UpdatedOn\":0,\"EnteredDate\":0,\"GoalFk\":\"f\",\"DeletedAt\":null}}"));
        var body = new StringContent($"{{\"Records\":[{records}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync(endpoint, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
