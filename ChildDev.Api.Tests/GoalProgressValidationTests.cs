using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ChildDev.Api.Models.Dtos;
using ChildDev.Api.Tests.Helpers;

namespace ChildDev.Api.Tests;

public class GoalProgressValidationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(string jwt, string accountGuid)> RegisterAsync(string nick)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(nick, "pinhash123"));
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        return (auth!.Jwt, auth.AccountGuid);
    }

    [Fact]
    public async Task SaveProgress_WithEmptyPayload_ReturnsBadRequest()
    {
        var (jwt, _) = await RegisterAsync("gpval_empty1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.PostAsync("/api/sync/goal-progress",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SaveProgress_WithVeryLongNextSteps_ReturnsUnprocessableEntity()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpval_longsteps1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var longValue = new string('x', 4001);
        var guid = Guid.NewGuid();
        var goalFk = Guid.NewGuid();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var response = await _client.PostAsync("/api/sync/goal-progress",
            new StringContent(
                $"{{\"Records\":[{{\"Guid\":\"{guid}\",\"AccountFk\":\"{accountGuid}\",\"GoalFk\":\"{goalFk}\",\"NextStepItems\":\"{longValue}\",\"UpdatedOn\":{ts},\"DeletedAt\":null}}],\"LastSyncAt\":0}}",
                Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task GetProgress_ReturnsOnlyProgressForRequestingAccount()
    {
        // Account A saves progress for a specific goal guid
        var (jwtA, accountGuidA) = await RegisterAsync("gpval_isoa1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtA);

        var sharedGoalGuid = Guid.NewGuid().ToString();
        var progressGuid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([
                new GoalProgressDto(progressGuid, accountGuidA, sharedGoalGuid, "account A step", null, ts, null)
            ], 0));

        // Account B uses the same goal guid — must see an empty delta (no cross-account leakage)
        var (jwtB, _) = await RegisterAsync("gpval_isob1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtB);

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(body!.Records, r => r.GoalFk == sharedGoalGuid);
    }

    [Fact]
    public async Task SaveProgress_SoftDelete_ExcludedFromOwnDeltaWhenQueriedWithUpdatedOnFilter()
    {
        // Save a progress note, then soft-delete it (DeletedAt == UpdatedOn).
        // Verify that a delta query with LastSyncAt >= the delete timestamp returns the record
        // with DeletedAt set (tombstone propagation), not silently hidden.
        var (jwt, accountGuid) = await RegisterAsync("gpval_softdel1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid().ToString();
        var goalGuid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Create the progress note
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([
                new GoalProgressDto(guid, accountGuid, goalGuid, "Step to remove", null, ts, null)
            ], 0));

        // Soft-delete it: UpdatedOn == DeletedAt
        var deletedAt = ts + 500;
        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([
                new GoalProgressDto(guid, accountGuid, goalGuid, null, null, deletedAt, deletedAt)
            ], 0));

        // Full delta (LastSyncAt = 0) must include the tombstone so clients can sync the deletion
        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        var stored = body!.Records.FirstOrDefault(r => r.Guid == guid);
        Assert.NotNull(stored);
        Assert.Equal(deletedAt, stored!.DeletedAt);
        Assert.Equal(deletedAt, stored.UpdatedOn);

        // A delta query that starts *after* the soft-delete timestamp must NOT include the record
        var filteredResponse = await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([], deletedAt));
        var filteredBody = await filteredResponse.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        Assert.DoesNotContain(filteredBody!.Records, r => r.Guid == guid);
    }
}
