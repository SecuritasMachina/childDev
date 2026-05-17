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
    [InlineData("/api/sync/journal",      "{\"Records\":[{\"Guid\":\"not-a-guid\",\"AccountFk\":\"a1\",\"UpdatedOn\":0,\"EnteredDate\":0,\"DeletedAt\":null}],\"LastSyncAt\":0}")]
    [InlineData("/api/sync/goal",         "{\"Records\":[{\"Guid\":\"not-a-guid\",\"AccountFk\":\"a1\",\"UpdatedOn\":0,\"EnteredDate\":0,\"DeletedAt\":null}],\"LastSyncAt\":0}")]
    [InlineData("/api/sync/goal-progress","{\"Records\":[{\"Guid\":\"not-a-guid\",\"AccountFk\":\"a1\",\"GoalFk\":\"f1\",\"UpdatedOn\":0,\"DeletedAt\":null}],\"LastSyncAt\":0}")]
    [InlineData("/api/sync/todo",         "{\"Records\":[{\"Guid\":\"not-a-guid\",\"AccountFk\":\"a1\",\"UpdatedOn\":0,\"DeletedAt\":null}],\"LastSyncAt\":0}")]
    public async Task Sync_InvalidGuid_Returns422(string endpoint, string body)
    {
        var jwt = await RegisterJwtAsync($"guidval_{endpoint.Replace("/", "_")}");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.PostAsync(endpoint,
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_GoalProgress_InvalidGoalFk_Returns422()
    {
        var jwt = await RegisterJwtAsync("goalfkval");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var body = new StringContent(
            "{\"Records\":[{\"Guid\":\"" + Guid.NewGuid() + "\",\"AccountFk\":\"a1\",\"GoalFk\":\"not-a-guid\",\"UpdatedOn\":0,\"DeletedAt\":null}],\"LastSyncAt\":0}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/goal-progress", body);

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

    [Fact]
    public async Task Sync_Journal_BlankNotes_Returns422()
    {
        var jwt = await RegisterJwtAsync("journalnotesval");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"Notes\":\"  \",\"EnteredDate\":0,\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/journal", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Goal_BlankGoalText_Returns422()
    {
        var jwt = await RegisterJwtAsync("goalgoaltextval");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"GoalText\":\"\",\"EnteredDate\":0,\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/goal", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Todo_FutureDueDate_Returns422()
    {
        var jwt = await RegisterJwtAsync("tododuedateval");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var farFutureMs = DateTimeOffset.UtcNow.AddYears(11).ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"Title\":\"test\",\"DueDate\":{farFutureMs},\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/todo", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
