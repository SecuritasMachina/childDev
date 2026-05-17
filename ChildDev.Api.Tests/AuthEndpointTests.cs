using System.Net;
using System.Net.Http.Json;
using ChildDev.Api.Tests.Helpers;

namespace ChildDev.Api.Tests;

public class AuthEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Register_Returns201_WithJwt()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            NickName = "testuser",
            PinHash = "hashedpin123"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(body!["Jwt"]);
        Assert.NotNull(body["AccountGuid"]);
    }

    [Fact]
    public async Task Register_DuplicateNickName_Returns409()
    {
        var payload = new { NickName = "dupeuser", PinHash = "hashedpin123" };
        await _client.PostAsJsonAsync("/api/auth/register", payload);
        var response = await _client.PostAsJsonAsync("/api/auth/register", payload);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Token_ValidCredentials_Returns200_WithJwt()
    {
        var nick = "tokenuser";
        var pin = "hashedpin123";
        await _client.PostAsJsonAsync("/api/auth/register", new { NickName = nick, PinHash = pin });
        var response = await _client.PostAsJsonAsync("/api/auth/token", new { NickName = nick, PinHash = pin });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(body!["Jwt"]);
    }

    [Fact]
    public async Task Token_WrongPin_Returns401()
    {
        var nick = "authuser";
        await _client.PostAsJsonAsync("/api/auth/register", new { NickName = nick, PinHash = "correcthash" });
        var response = await _client.PostAsJsonAsync("/api/auth/token", new { NickName = nick, PinHash = "wronghash" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_EmptyNickName_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            NickName = "",
            PinHash = "hashedpin123"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WhitespaceNickName_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            NickName = "   ",
            PinHash = "hashedpin123"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_TooLongNickName_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            NickName = new string('x', 51),
            PinHash = "hashedpin123"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_TooLongPinHash_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            NickName = "validuser3",
            PinHash = new string('x', 201)
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_EmptyPinHash_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            NickName = "validuser",
            PinHash = ""
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WhitespacePinHash_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            NickName = "validuser2",
            PinHash = "   "
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Token_NickNameWithSurroundingSpaces_StillAuthenticates()
    {
        var nick = "trimtestuser";
        await _client.PostAsJsonAsync("/api/auth/register", new { NickName = nick, PinHash = "correcthash" });
        var response = await _client.PostAsJsonAsync("/api/auth/token",
            new { NickName = $"  {nick}  ", PinHash = "correcthash" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Token_UnknownUser_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/token",
            new { NickName = "nobody_registered_this_nick", PinHash = "anyhash" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_NickNameWithSurroundingSpaces_StoredTrimmed()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new { NickName = "  trimmeduser  ", PinHash = "testhash123" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Token lookup with exact trimmed name succeeds, proving stored value was trimmed
        var tokenResponse = await _client.PostAsJsonAsync("/api/auth/token",
            new { NickName = "trimmeduser", PinHash = "testhash123" });
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
    }

    [Fact]
    public async Task Register_SpacePaddedNickName_DetectsConflictWithExistingTrimmedName()
    {
        // Register exact name first
        await _client.PostAsJsonAsync("/api/auth/register",
            new { NickName = "conflictuser", PinHash = "hash1" });

        // Attempt to register space-padded version — should detect duplicate (both resolve to "conflictuser")
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new { NickName = "  conflictuser  ", PinHash = "hash2" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
