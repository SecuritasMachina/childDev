using System.Net.Http.Json;

namespace LevelUp.Services;

/// <summary>
/// Posts mobile telemetry directly to the external AnalyticsHub (bizeyes) collect API.
/// All sends are fire-and-forget and swallow errors so analytics never affects the UI.
/// A session is created lazily on first use and cached for the app lifetime.
/// </summary>
public class BizEyesAnalyticsService(
    IHttpClientFactory httpFactory,
    AccountService accountService,
    IDeviceMetadataProvider deviceInfo)
{
    private string? _sessionToken;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

    /// <summary>
    /// Track a custom event (button click, feature use, etc.).
    /// Privacy: the free-text <c>context</c> is NOT forwarded (may carry goal/journal/todo
    /// text); only the event name + a coarse "mobile" category are sent.
    /// </summary>
    public void TrackEvent(string name, string? context = null) => FireAndForget(async () =>
    {
        var token = await EnsureSessionAsync();
        await PostAsync("event", new
        {
            apiKey = BizEyesConfig.ApiKey,
            name,
            category = "mobile",
            sessionToken = token,
            environment = BizEyesConfig.Environment
        });
    });

    /// <summary>Track a screen view (mobile equivalent of a page view).</summary>
    public void TrackScreenView(string path) => FireAndForget(async () =>
    {
        var token = await EnsureSessionAsync();
        await PostAsync("pageview", new
        {
            apiKey = BizEyesConfig.ApiKey,
            path,
            sessionToken = token,
            environment = BizEyesConfig.Environment
        });
    });

    /// <summary>
    /// Track an exception (type only). Privacy: message and stack trace are NOT forwarded —
    /// they can contain file paths or user input. Only the type name is sent.
    /// </summary>
    public void TrackException(Exception ex, bool isHandled) => FireAndForget(() => PostAsync("exception", new
    {
        apiKey = BizEyesConfig.ApiKey,
        type = ex.GetType().FullName,
        severity = "Error",
        isHandled,
        environment = BizEyesConfig.Environment
    }));

    private async Task<string?> EnsureSessionAsync()
    {
        if (_sessionToken is not null) return _sessionToken;
        await _sessionLock.WaitAsync();
        try
        {
            if (_sessionToken is not null) return _sessionToken;

            string? userId = null;
            try { userId = (await accountService.GetAccountAsync())?.Guid; } catch { /* best effort */ }

            var resp = await Client().PostAsJsonAsync($"{BizEyesConfig.BaseUrl}/api/collect/session", new
            {
                apiKey = BizEyesConfig.ApiKey,
                userId = Hash(userId), // pseudonymous: don't expose the raw account guid
                os = deviceInfo.Os,
                device = deviceInfo.Device,
                environment = BizEyesConfig.Environment
            });
            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadFromJsonAsync<SessionResponse>();
                _sessionToken = body?.SessionToken;
            }
            return _sessionToken;
        }
        catch
        {
            return null;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private HttpClient Client()
    {
        var c = httpFactory.CreateClient("bizeyes");
        c.Timeout = TimeSpan.FromSeconds(5);
        return c;
    }

    private async Task PostAsync(string path, object payload)
    {
        try { await Client().PostAsJsonAsync($"{BizEyesConfig.BaseUrl}/api/collect/{path}", payload); }
        catch { /* fire-and-forget */ }
    }

    private void FireAndForget(Func<Task> work)
    {
        if (!BizEyesConfig.Enabled) return;
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
