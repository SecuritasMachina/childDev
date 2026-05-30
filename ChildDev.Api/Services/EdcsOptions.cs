namespace ChildDev.Api.Services;

/// <summary>
/// Connection settings for EDCS (Enterprise Distributed Config System), bound from the "Edcs"
/// configuration section / environment. All values are optional: when any required field is
/// blank, <see cref="EdcsConfigClient"/> skips EDCS entirely (soft dependency).
/// Secrets (<see cref="ClientSecret"/>) come from env/secret files, never from committed config.
/// </summary>
public sealed class EdcsOptions
{
    public const string SectionName = "Edcs";

    /// <summary>Identity STS base URL (issues OAuth2 tokens), e.g. https://auth.securitasmachina.org</summary>
    public string StsUrl { get; set; } = string.Empty;

    /// <summary>AppConfig API base URL, e.g. https://config.securitasmachina.org</summary>
    public string AppConfigUrl { get; set; } = string.Empty;

    /// <summary>Client-credentials client id for this app's EDCS service identity.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Client-credentials secret (supply via env/secret file, never committed).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Requested OAuth2 scope; read-only access to AppConfig is sufficient.</summary>
    public string Scope { get; set; } = "appconfig:read";

    /// <summary>EDCS application id (namespace) this app's config lives under.</summary>
    public string AppId { get; set; } = "childdev";
}
