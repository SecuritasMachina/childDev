using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ChildDev.Api.Services;

/// <summary>
/// Reads a single application-config value from EDCS (Enterprise Distributed Config System):
/// obtains a short-lived OAuth2 token from the STS via the client-credentials grant, then
/// reads <c>GET {appConfigUrl}/v1/app-config/{appId}/{key}</c> with that bearer token.
///
/// EDCS is a SOFT dependency. Every failure mode — not configured, STS/AppConfig unreachable,
/// timeout, auth/scope error, 404 (key not yet provisioned), or malformed payload — yields a
/// <c>null</c> result and never throws, so a config-store outage can never block app startup.
/// </summary>
public sealed class EdcsConfigClient
{
    private readonly HttpClient _http;

    /// <param name="http">
    /// Caller supplies the HttpClient (injectable for testing). A short Timeout should be set by
    /// the caller so a slow EDCS cannot stall startup.
    /// </param>
    public EdcsConfigClient(HttpClient http) => _http = http;

    /// <summary>
    /// Returns the config value, or <c>null</c> on any failure / when EDCS is not configured.
    /// </summary>
    public async Task<string?> TryGetValueAsync(
        EdcsOptions o, string appId, string key,
        Action<string>? warn = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(o.StsUrl) || string.IsNullOrWhiteSpace(o.AppConfigUrl)
            || string.IsNullOrWhiteSpace(o.ClientId) || string.IsNullOrWhiteSpace(o.ClientSecret))
            return null; // EDCS not configured — silently skip.

        try
        {
            var token = await GetTokenAsync(o, ct);
            if (string.IsNullOrEmpty(token))
            {
                warn?.Invoke($"EDCS token request failed; continuing without '{key}'.");
                return null;
            }

            var url = $"{o.AppConfigUrl.TrimEnd('/')}/v1/app-config/{Uri.EscapeDataString(appId)}/{Uri.EscapeDataString(key)}";
            using var msg = new HttpRequestMessage(HttpMethod.Get, url);
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await _http.SendAsync(msg, ct);
            if (!resp.IsSuccessStatusCode)
            {
                warn?.Invoke($"EDCS lookup of '{key}' returned {(int)resp.StatusCode}; continuing without it.");
                return null;
            }

            var dto = await resp.Content.ReadFromJsonAsync<ConfigValueDto>(cancellationToken: ct);
            return string.IsNullOrEmpty(dto?.Value) ? null : dto.Value;
        }
        catch (Exception ex)
        {
            // Never let a config-store outage break startup.
            warn?.Invoke($"EDCS unavailable for '{key}' ({ex.GetType().Name}); continuing without it.");
            return null;
        }
    }

    private async Task<string?> GetTokenAsync(EdcsOptions o, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = o.ClientId,
            ["client_secret"] = o.ClientSecret,
            ["scope"] = o.Scope,
        });

        using var resp = await _http.PostAsync($"{o.StsUrl.TrimEnd('/')}/connect/token", form, ct);
        if (!resp.IsSuccessStatusCode) return null;

        var tok = await resp.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
        return tok?.AccessToken;
    }

    private sealed record ConfigValueDto(
        [property: JsonPropertyName("value")] string? Value);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken);
}
