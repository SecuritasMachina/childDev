using System.Net;
using System.Text;
using ChildDev.Api.Services;
using Xunit;

namespace ChildDev.Api.Tests;

public class EdcsConfigClientTests
{
    private static EdcsOptions FullOpts() => new()
    {
        StsUrl = "https://sts.test",
        AppConfigUrl = "https://config.test",
        ClientId = "childdev",
        ClientSecret = "shh",
        Scope = "appconfig:read",
        AppId = "childdev",
    };

    // Routes responses per request URL; records calls. Lets us simulate every EDCS failure mode.
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(responder(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static EdcsConfigClient Client(StubHandler h) => new(new HttpClient(h));

    [Fact]
    public async Task HappyPath_ReturnsValue()
    {
        var h = new StubHandler(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/connect/token")
                ? Json(HttpStatusCode.OK, """{"access_token":"jwt-123"}""")
                : Json(HttpStatusCode.OK, """{"key":"analytics.bizeyes.apikey","value":"ah_new_secret"}"""));

        var result = await Client(h).TryGetValueAsync(FullOpts(), "childdev", "analytics.bizeyes.apikey");
        Assert.Equal("ah_new_secret", result);
    }

    [Fact]
    public async Task NotConfigured_ReturnsNull_WithoutAnyHttpCall()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));
        var result = await Client(h).TryGetValueAsync(new EdcsOptions(), "childdev", "k");
        Assert.Null(result);
        Assert.Equal(0, h.Calls); // never even hit the network
    }

    [Fact]
    public async Task TokenEndpointFails_ReturnsNull()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}"""));
        Assert.Null(await Client(h).TryGetValueAsync(FullOpts(), "childdev", "k"));
    }

    [Fact]
    public async Task ConfigKeyMissing_404_ReturnsNull()
    {
        // This is the expected early state before the value is provisioned in EDCS.
        var h = new StubHandler(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/connect/token")
                ? Json(HttpStatusCode.OK, """{"access_token":"jwt-123"}""")
                : Json(HttpStatusCode.NotFound, """{"error":"not found"}"""));
        Assert.Null(await Client(h).TryGetValueAsync(FullOpts(), "childdev", "k"));
    }

    [Fact]
    public async Task ForbiddenScope_403_ReturnsNull()
    {
        var h = new StubHandler(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/connect/token")
                ? Json(HttpStatusCode.OK, """{"access_token":"jwt-123"}""")
                : Json(HttpStatusCode.Forbidden, ""));
        Assert.Null(await Client(h).TryGetValueAsync(FullOpts(), "childdev", "k"));
    }

    [Fact]
    public async Task MalformedPayload_ReturnsNull()
    {
        var h = new StubHandler(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/connect/token")
                ? Json(HttpStatusCode.OK, """{"access_token":"jwt-123"}""")
                : Json(HttpStatusCode.OK, "not-json-at-all"));
        Assert.Null(await Client(h).TryGetValueAsync(FullOpts(), "childdev", "k"));
    }

    [Fact]
    public async Task EmptyValue_ReturnsNull()
    {
        var h = new StubHandler(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/connect/token")
                ? Json(HttpStatusCode.OK, """{"access_token":"jwt-123"}""")
                : Json(HttpStatusCode.OK, """{"key":"k","value":""}"""));
        Assert.Null(await Client(h).TryGetValueAsync(FullOpts(), "childdev", "k"));
    }

    [Fact]
    public async Task NetworkException_ReturnsNull_NeverThrows()
    {
        var h = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var warned = false;
        var result = await Client(h).TryGetValueAsync(FullOpts(), "childdev", "k", warn: _ => warned = true);
        Assert.Null(result);
        Assert.True(warned);
    }
}
