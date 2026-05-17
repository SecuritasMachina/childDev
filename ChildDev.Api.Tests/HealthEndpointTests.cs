using System.Net;
using ChildDev.Api.Tests.Helpers;

namespace ChildDev.Api.Tests;

public class HealthEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_Returns200()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_EchoesXRequestIdHeader()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/health");
        req.Headers.Add("X-Request-ID", "test-trace-123");
        var response = await _client.SendAsync(req);
        Assert.Equal("test-trace-123", response.Headers.GetValues("X-Request-ID").First());
    }

    [Fact]
    public async Task Request_GeneratesXRequestIdWhenNotProvided()
    {
        var response = await _client.GetAsync("/health");
        Assert.True(response.Headers.Contains("X-Request-ID"));
        Assert.NotEmpty(response.Headers.GetValues("X-Request-ID").First());
    }
}
