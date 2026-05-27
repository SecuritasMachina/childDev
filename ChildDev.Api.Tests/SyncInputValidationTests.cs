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
    public async Task Sync_GoalProgress_BlankNextStepItems_Returns422()
    {
        var jwt = await RegisterJwtAsync("gpnextstepval");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid();
        var goalFk = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"GoalFk\":\"{goalFk}\",\"NextStepItems\":\"  \",\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/goal-progress", body);

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

    [Theory]
    [InlineData("/api/sync/journal", "Notes", 10001)]
    [InlineData("/api/sync/goal", "GoalText", 2001)]
    public async Task Sync_FieldTooLong_Returns422(string endpoint, string field, int length)
    {
        var jwt = await RegisterJwtAsync($"toolong_{endpoint.Replace("/", "_")}_{field}");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var longValue = new string('x', length);
        var guid = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"{field}\":\"{longValue}\",\"EnteredDate\":0,\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync(endpoint, body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData("Activity", 256)]
    [InlineData("Mood", 51)]
    [InlineData("Tags", 501)]
    public async Task Sync_Journal_AuxFieldTooLong_Returns422(string field, int length)
    {
        var jwt = await RegisterJwtAsync($"journalaux_{field}");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var longValue = new string('x', length);
        var guid = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"Notes\":\"x\",\"{field}\":\"{longValue}\",\"EnteredDate\":0,\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/journal", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData("Title", 501)]
    [InlineData("Notes", 2001)]
    public async Task Sync_Todo_FieldTooLong_Returns422(string field, int length)
    {
        var jwt = await RegisterJwtAsync($"todofield_{field}");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var longValue = new string('x', length);
        var guid = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"Title\":\"x\",\"{field}\":\"{longValue}\",\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/todo", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Goal_MeasurableOutcomeTooLong_Returns422()
    {
        var jwt = await RegisterJwtAsync("goalmotoolong");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var longValue = new string('x', 2001);
        var guid = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"GoalText\":\"valid\",\"MeasurableOutcome\":\"{longValue}\",\"EnteredDate\":0,\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/goal", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Goal_FutureExpirationDate_Returns422()
    {
        var jwt = await RegisterJwtAsync("goalexpirationval");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var farFutureMs = DateTimeOffset.UtcNow.AddYears(11).ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"GoalText\":\"x\",\"ExpirationDate\":{farFutureMs},\"EnteredDate\":0,\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/goal", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Goal_FutureCompletionDate_Returns422()
    {
        var jwt = await RegisterJwtAsync("goalcompletionval");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var farFutureMs = DateTimeOffset.UtcNow.AddYears(11).ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"GoalText\":\"x\",\"CompletionDate\":{farFutureMs},\"EnteredDate\":0,\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/goal", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Todo_FutureCompletedAt_Returns422()
    {
        var jwt = await RegisterJwtAsync("todocompletedatval");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var farFutureMs = DateTimeOffset.UtcNow.AddYears(11).ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"Title\":\"x\",\"CompletedAt\":{farFutureMs},\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/todo", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_GoalProgress_FutureNextMeetingDate_Returns422()
    {
        var jwt = await RegisterJwtAsync("gpnextmeetingval");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var farFutureMs = DateTimeOffset.UtcNow.AddYears(11).ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid();
        var goalFk = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"GoalFk\":\"{goalFk}\",\"NextStepItems\":\"x\",\"NextMeetingDate\":{farFutureMs},\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/goal-progress", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_GoalProgress_NextStepItemsTooLong_Returns422()
    {
        var jwt = await RegisterJwtAsync("gpnextsteptoolong");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var longValue = new string('x', 2001);
        var guid = Guid.NewGuid();
        var goalFk = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"GoalFk\":\"{goalFk}\",\"NextStepItems\":\"{longValue}\",\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/goal-progress", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/sync/journal")]
    [InlineData("/api/sync/goal")]
    public async Task Sync_FutureEnteredDate_Returns422(string endpoint)
    {
        var jwt = await RegisterJwtAsync($"futentdate_{endpoint.Replace("/", "_")}");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var farFutureMs = DateTimeOffset.UtcNow.AddYears(11).ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"Notes\":\"x\",\"GoalText\":\"x\",\"EnteredDate\":{farFutureMs},\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync(endpoint, body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Todo_BlankTitle_Returns422()
    {
        var jwt = await RegisterJwtAsync("todotitleval");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"Title\":\" \",\"UpdatedOn\":0,\"DeletedAt\":null,\"CompletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/todo", body);

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

    [Theory]
    [InlineData("/api/sync/journal",
        "{\"Records\":[{\"Guid\":\"PLACEHOLDER\",\"AccountFk\":\"a1\",\"Notes\":\"x\",\"EnteredDate\":0,\"UpdatedOn\":0,\"DeletedAt\":null},{\"Guid\":\"PLACEHOLDER\",\"AccountFk\":\"a1\",\"Notes\":\"y\",\"EnteredDate\":0,\"UpdatedOn\":1,\"DeletedAt\":null}],\"LastSyncAt\":0}")]
    [InlineData("/api/sync/goal",
        "{\"Records\":[{\"Guid\":\"PLACEHOLDER\",\"AccountFk\":\"a1\",\"GoalText\":\"x\",\"EnteredDate\":0,\"UpdatedOn\":0,\"DeletedAt\":null},{\"Guid\":\"PLACEHOLDER\",\"AccountFk\":\"a1\",\"GoalText\":\"y\",\"EnteredDate\":0,\"UpdatedOn\":1,\"DeletedAt\":null}],\"LastSyncAt\":0}")]
    [InlineData("/api/sync/goal-progress",
        "{\"Records\":[{\"Guid\":\"PLACEHOLDER\",\"AccountFk\":\"a1\",\"GoalFk\":\"GOALFK\",\"NextStepItems\":\"x\",\"UpdatedOn\":0,\"DeletedAt\":null},{\"Guid\":\"PLACEHOLDER\",\"AccountFk\":\"a1\",\"GoalFk\":\"GOALFK\",\"NextStepItems\":\"y\",\"UpdatedOn\":1,\"DeletedAt\":null}],\"LastSyncAt\":0}")]
    [InlineData("/api/sync/todo",
        "{\"Records\":[{\"Guid\":\"PLACEHOLDER\",\"AccountFk\":\"a1\",\"Title\":\"x\",\"UpdatedOn\":0,\"DeletedAt\":null},{\"Guid\":\"PLACEHOLDER\",\"AccountFk\":\"a1\",\"Title\":\"y\",\"UpdatedOn\":1,\"DeletedAt\":null}],\"LastSyncAt\":0}")]
    public async Task Sync_DuplicateGuid_Returns422(string endpoint, string bodyTemplate)
    {
        var jwt = await RegisterJwtAsync($"dupguid_{endpoint.Replace("/", "_")}");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid().ToString();
        var goalFk = Guid.NewGuid().ToString();
        var body = new StringContent(
            bodyTemplate.Replace("PLACEHOLDER", guid).Replace("GOALFK", goalFk),
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync(endpoint, body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/sync/journal")]
    [InlineData("/api/sync/goal")]
    [InlineData("/api/sync/goal-progress")]
    [InlineData("/api/sync/todo")]
    public async Task Sync_NoAuth_Returns401(string endpoint)
    {
        var client = factory.CreateClient();
        var body = new StringContent("{\"Records\":[],\"LastSyncAt\":0}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync(endpoint, body);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Journal_NotesTooLong_Returns422()
    {
        var jwt = await RegisterJwtAsync("journalnotestoolong");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var longNotes = new string('x', 10_001);
        var guid = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"Notes\":\"{longNotes}\",\"EnteredDate\":0,\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/journal", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Journal_ActivityTooLong_Returns422()
    {
        var jwt = await RegisterJwtAsync("journalactivitytoolong");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var longActivity = new string('x', 256);
        var guid = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"Notes\":\"valid\",\"Activity\":\"{longActivity}\",\"EnteredDate\":0,\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/journal", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Journal_MoodTooLong_Returns422()
    {
        var jwt = await RegisterJwtAsync("journalmoodtoolong");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var longMood = new string('x', 51);
        var guid = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"Notes\":\"valid\",\"Mood\":\"{longMood}\",\"EnteredDate\":0,\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/journal", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Journal_TagsTooLong_Returns422()
    {
        var jwt = await RegisterJwtAsync("journaltagstoolong");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var longTags = new string('x', 501);
        var guid = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"Notes\":\"valid\",\"Tags\":\"{longTags}\",\"EnteredDate\":0,\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/journal", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Goal_GoalTextTooLong_Returns422()
    {
        var jwt = await RegisterJwtAsync("goalgoaltexttoolong");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var longGoalText = new string('x', 2_001);
        var guid = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"GoalText\":\"{longGoalText}\",\"EnteredDate\":0,\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/goal", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Todo_TitleTooLong_Returns422()
    {
        var jwt = await RegisterJwtAsync("todotitletoolong");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var longTitle = new string('x', 501);
        var guid = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"Title\":\"{longTitle}\",\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/todo", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Todo_NotesTooLong_Returns422()
    {
        var jwt = await RegisterJwtAsync("todonotestoolong");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var longNotes = new string('x', 2_001);
        var guid = Guid.NewGuid();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"Title\":\"valid\",\"Notes\":\"{longNotes}\",\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/todo", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Journal_SoftDeletedWithBlankNotes_IsAccepted()
    {
        var jwt = await RegisterJwtAsync("journalsoftdelnotes");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid();
        var deletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"Notes\":null,\"EnteredDate\":0,\"UpdatedOn\":{deletedAt},\"DeletedAt\":{deletedAt}}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/journal", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Goal_SoftDeletedWithBlankGoalText_IsAccepted()
    {
        var jwt = await RegisterJwtAsync("goalsoftdeltext");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid();
        var deletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"GoalText\":null,\"EnteredDate\":0,\"UpdatedOn\":{deletedAt},\"DeletedAt\":{deletedAt}}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/goal", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Sync_GoalProgress_SoftDeletedWithBlankNextStepItems_IsAccepted()
    {
        var jwt = await RegisterJwtAsync("gpsoftdelnextsteps");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid();
        var goalFk = Guid.NewGuid();
        var deletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"GoalFk\":\"{goalFk}\",\"NextStepItems\":null,\"UpdatedOn\":{deletedAt},\"DeletedAt\":{deletedAt}}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/goal-progress", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Todo_SoftDeletedWithBlankTitle_IsAccepted()
    {
        var jwt = await RegisterJwtAsync("todosoftdeltitle");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid();
        var deletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"Title\":null,\"UpdatedOn\":{deletedAt},\"DeletedAt\":{deletedAt}}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/todo", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Todo_CompletedWithBlankTitle_IsAccepted()
    {
        var jwt = await RegisterJwtAsync("todocompletedtitle");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid();
        var completedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"Title\":null,\"UpdatedOn\":{completedAt},\"CompletedAt\":{completedAt},\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/todo", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Goal_CompletedWithBlankGoalText_IsAccepted()
    {
        var jwt = await RegisterJwtAsync("goalcompletedtext");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid();
        var completionDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"GoalText\":null,\"EnteredDate\":0,\"CompletionDate\":{completionDate},\"UpdatedOn\":{completionDate},\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/goal", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Goal_FutureNextMeetingDate_Returns422()
    {
        var jwt = await RegisterJwtAsync("goalfuturemeeting");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid();
        var farFutureMs = DateTimeOffset.UtcNow.AddYears(11).ToUnixTimeMilliseconds();
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"GoalText\":\"test\",\"NextMeetingDate\":{farFutureMs},\"EnteredDate\":0,\"UpdatedOn\":0,\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/goal", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_ExactlyMaxBatchSize_Returns200()
    {
        var jwt = await RegisterJwtAsync("batch_500");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1000;
        var records = string.Join(",", Enumerable.Range(0, 500)
            .Select(_ => $"{{\"Guid\":\"{Guid.NewGuid()}\",\"AccountFk\":\"ignore\",\"Notes\":\"note\",\"EnteredDate\":{ts},\"UpdatedOn\":{ts},\"DeletedAt\":null}}"));
        var body = new StringContent($"{{\"Records\":[{records}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/journal", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Goal_CategoryTooLong_Returns422()
    {
        var jwt = await RegisterJwtAsync("goalcategorytoolong");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var longCategory = new string('C', 51);
        var guid = Guid.NewGuid();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1000;
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"GoalText\":\"valid\",\"Category\":\"{longCategory}\",\"EnteredDate\":{ts},\"UpdatedOn\":{ts},\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/goal", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Sync_Goal_CategoryExactly50Chars_IsAccepted()
    {
        var jwt = await RegisterJwtAsync("goalcategoryexact50");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var exactCategory = new string('C', 50);
        var guid = Guid.NewGuid();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1000;
        var body = new StringContent(
            $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"a1\",\"GoalText\":\"valid\",\"Category\":\"{exactCategory}\",\"EnteredDate\":{ts},\"UpdatedOn\":{ts},\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/sync/goal", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
