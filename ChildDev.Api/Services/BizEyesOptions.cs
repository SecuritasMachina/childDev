namespace ChildDev.Api.Services;

/// <summary>
/// Configuration for forwarding telemetry to the external AnalyticsHub (bizeyes) service.
/// Bound from the "BizEyes" configuration section. The API key is supplied via config/env
/// (never committed) — when missing or <see cref="Enabled"/> is false, forwarding is a no-op.
/// </summary>
public class BizEyesOptions
{
    public const string SectionName = "BizEyes";

    /// <summary>Master switch. Forwarding only happens when true AND an ApiKey is set.</summary>
    public bool Enabled { get; set; }

    /// <summary>AnalyticsHub base URL.</summary>
    public string BaseUrl { get; set; } = "https://bizeyes.securitasmachina.org";

    /// <summary>AnalyticsHub application API key (ah_live_...). Supply via env/secrets, not source.</summary>
    public string ApiKey { get; set; } = string.Empty;
}
