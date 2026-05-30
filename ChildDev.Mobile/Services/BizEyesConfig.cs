namespace LevelUp.Services;

/// <summary>
/// Configuration for the external AnalyticsHub (bizeyes) telemetry service. The mobile app
/// posts directly to bizeyes, so the API key necessarily ships in the app. The
/// <see cref="Environment"/> tag is "Dev" for Debug builds and "Production" for Release,
/// giving AnalyticsHub the dev/prod separation.
/// </summary>
public static partial class BizEyesConfig
{
    public const bool Enabled = true;
    public const string BaseUrl = "https://bizeyes.securitasmachina.org";

    // Mobile (Android) AnalyticsHub app key. The value is injected at build time by a
    // gitignored partial (BizEyesConfig.Secret.cs) so it is never committed to source.
    // Empty on a clean checkout (analytics simply gets an empty key); the real key is
    // supplied locally / in the build pipeline via the gitignored partial.
    public static string ApiKey { get; private set; } = string.Empty;

#if DEBUG
    public const string Environment = "Dev";
#else
    public const string Environment = "Production";
#endif

    static BizEyesConfig() => InitSecrets();

    // Implemented only by the gitignored BizEyesConfig.Secret.cs; the call is elided when absent.
    static partial void InitSecrets();
}
