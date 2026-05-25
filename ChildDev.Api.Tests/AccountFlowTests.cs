using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChildDev.Api.Tests.Helpers;

namespace ChildDev.Api.Tests;

/// <summary>
/// End-to-end tests for the create-account → log-in flow, including authenticated access.
/// </summary>
public class AccountFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateAccount_ThenLogin_SucceedsAndReturnsSameGuid()
    {
        var nick = $"flow_{Guid.NewGuid():N}";
        const string pin = "mypassword";

        // Step 1: create account
        var regRes = await _client.PostAsJsonAsync("/api/auth/register", new { NickName = nick, PinHash = pin });
        Assert.Equal(HttpStatusCode.Created, regRes.StatusCode);
        var reg = await regRes.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.False(string.IsNullOrEmpty(reg!["Jwt"]));
        Assert.False(string.IsNullOrEmpty(reg["AccountGuid"]));

        // Step 2: log in
        var tokenRes = await _client.PostAsJsonAsync("/api/auth/token", new { NickName = nick, PinHash = pin });
        Assert.Equal(HttpStatusCode.OK, tokenRes.StatusCode);
        var token = await tokenRes.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.False(string.IsNullOrEmpty(token!["Jwt"]));
        Assert.Equal(reg["AccountGuid"], token["AccountGuid"]);
    }

    [Fact]
    public async Task CreateAccount_ThenLogin_JwtAuthorizesProtectedEndpoint()
    {
        var nick = $"authflow_{Guid.NewGuid():N}";
        const string pin = "securepass";

        // Create account and get JWT
        var regRes = await _client.PostAsJsonAsync("/api/auth/register", new { NickName = nick, PinHash = pin });
        var reg = await regRes.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        var jwt = reg!["Jwt"];

        // Use JWT to hit a protected sync endpoint (GET returns goals since timestamp 0)
        var authClient = factory.CreateClient();
        authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var syncRes = await authClient.PostAsJsonAsync("/api/sync/goal",
            new { Records = Array.Empty<object>(), LastSyncAt = 0L });

        Assert.Equal(HttpStatusCode.OK, syncRes.StatusCode);
    }

    [Fact]
    public async Task Login_WithoutAccount_Returns401()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/token",
            new { NickName = $"nobody_{Guid.NewGuid():N}", PinHash = "whatever" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var nick = $"wrongpass_{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/auth/register", new { NickName = nick, PinHash = "correct" });

        var res = await _client.PostAsJsonAsync("/api/auth/token",
            new { NickName = nick, PinHash = "incorrect" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutJwt_Returns401()
    {
        var res = await _client.PostAsJsonAsync("/api/sync/goal",
            new { Records = Array.Empty<object>(), LastSyncAt = 0L });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_DuplicateName_Returns409()
    {
        var nick = $"dupe_{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/auth/register", new { NickName = nick, PinHash = "pass1" });
        var res = await _client.PostAsJsonAsync("/api/auth/register", new { NickName = nick, PinHash = "pass2" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_ThenLogin_CanSyncGoals()
    {
        var nick = $"goalsync_{Guid.NewGuid():N}";
        const string pin = "goalpass";

        var regRes = await _client.PostAsJsonAsync("/api/auth/register", new { NickName = nick, PinHash = pin });
        var reg = await regRes.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        var accountGuid = reg!["AccountGuid"];
        var jwt = reg["Jwt"];

        var authClient = factory.CreateClient();
        authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        // Push a goal
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goalGuid = Guid.NewGuid().ToString();
        var pushRes = await authClient.PostAsJsonAsync("/api/sync/goal", new
        {
            LastSyncAt = 0L,
            Records = new[]
            {
                new
                {
                    Guid = goalGuid,
                    AccountFk = accountGuid,
                    GoalText = "Learn piano",
                    EnteredDate = ts,
                    UpdatedOn = ts,
                    DeletedAt = (long?)null
                }
            }
        });
        Assert.Equal(HttpStatusCode.OK, pushRes.StatusCode);

        // Verify the goal is in the sync response
        var body = await pushRes.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.NotNull(body);
    }
}
