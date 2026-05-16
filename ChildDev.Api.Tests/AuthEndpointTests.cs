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
}
