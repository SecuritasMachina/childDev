using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace ChildDev.Api.Services;

/// <summary>
/// Forwards telemetry to the external AnalyticsHub (bizeyes) collect API.
///
/// Registered as a singleton and uses <see cref="IHttpClientFactory"/> so calls are safe to
/// fire-and-forget from request/circuit-scoped callers without capturing a disposed scope.
/// Every method is best-effort: failures are swallowed so analytics never affects the app.
/// The <c>environment</c> field is "Dev" in Development and "Production" otherwise, satisfying
/// the dev/prod separation in AnalyticsHub.
/// </summary>
public class BizEyesClient(
    IHttpClientFactory httpFactory,
    IOptions<BizEyesOptions> options,
    IWebHostEnvironment hostEnv,
    ILogger<BizEyesClient> logger)
{
    private readonly BizEyesOptions _o = options.Value;

    // One AnalyticsHub session per app-user (keyed by account guid; "anon" when unknown).
    private readonly ConcurrentDictionary<string, string> _sessionTokens = new();

    public bool Enabled => _o.Enabled && !string.IsNullOrWhiteSpace(_o.ApiKey);

    private string Environment => hostEnv.IsDevelopment() ? "Dev" : "Production";

    private HttpClient Client()
    {
        var c = httpFactory.CreateClient("bizeyes");
        c.Timeout = TimeSpan.FromSeconds(5);
        return c;
    }

    /// <summary>
    /// Track a custom event. Lazily creates a session for the user first.
    /// Privacy: the free-text <c>context</c> (e.g. todo titles, journal snippets, search
    /// queries) is intentionally NOT forwarded — only the event name and the page/route as
    /// category. The first-party MariaDB store still keeps full context.
    /// </summary>
    public void TrackEvent(string name, string? accountGuid, string? category)
        => FireAndForget(async () =>
        {
            var token = await EnsureSessionAsync(accountGuid);
            await PostAsync("event", new
            {
                apiKey = _o.ApiKey,
                name,
                category,
                sessionToken = token,
                environment = Environment
            });
        });

    /// <summary>Track a page view. Lazily creates a session for the user first.</summary>
    public void TrackPageView(string path, string? accountGuid, string? title)
        => FireAndForget(async () =>
        {
            var token = await EnsureSessionAsync(accountGuid);
            await PostAsync("pageview", new
            {
                apiKey = _o.ApiKey,
                path,
                title,
                sessionToken = token,
                environment = Environment
            });
        });

    /// <summary>
    /// Track an exception (type only). No session required.
    /// Privacy: the exception message and stack trace are NOT forwarded — they can contain
    /// file paths, user input, or other system detail. Only the type name is sent.
    /// </summary>
    public void TrackException(Exception ex, bool isHandled, string severity = "Error")
        => FireAndForget(() => PostAsync("exception", new
        {
            apiKey = _o.ApiKey,
            type = ex.GetType().FullName,
            severity,
            isHandled,
            environment = Environment
        }));

    private async Task<string?> EnsureSessionAsync(string? accountGuid)
    {
        var key = string.IsNullOrEmpty(accountGuid) ? "anon" : accountGuid;
        if (_sessionTokens.TryGetValue(key, out var existing)) return existing;

        try
        {
            var resp = await Client().PostAsJsonAsync($"{_o.BaseUrl}/api/collect/session", new
            {
                apiKey = _o.ApiKey,
                userId = Hash(accountGuid), // pseudonymous: don't expose the raw account guid
                browser = "Web",
                device = "Web",
                environment = Environment
            });
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadFromJsonAsync<SessionResponse>();
            var token = body?.SessionToken;
            if (!string.IsNullOrEmpty(token)) _sessionTokens[key] = token;
            return token;
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "bizeyes session create failed");
            return null;
        }
    }

    private async Task PostAsync(string path, object payload)
    {
        try
        {
            await Client().PostAsJsonAsync($"{_o.BaseUrl}/api/collect/{path}", payload);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "bizeyes {Path} post failed", path);
        }
    }

    private void FireAndForget(Func<Task> work)
    {
        if (!Enabled) return;
        _ = Task.Run(work);
    }

    /// <summary>Pseudonymous, stable, non-reversible id for a value (first 16 hex of SHA-256).</summary>
    private static string? Hash(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private record SessionResponse(string SessionToken);
}
