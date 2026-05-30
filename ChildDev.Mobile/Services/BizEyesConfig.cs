namespace LevelUp.Services;

/// <summary>
/// Configuration for the external AnalyticsHub (bizeyes) telemetry service. The mobile app
/// posts directly to bizeyes, so the API key necessarily ships in the app. The
/// <see cref="Environment"/> tag is "Dev" for Debug builds and "Production" for Release,
/// giving AnalyticsHub the dev/prod separation.
/// </summary>
public static class BizEyesConfig
{
    public const bool Enabled = true;
    public const string BaseUrl = "https://bizeyes.securitasmachina.org";

    // Mobile (Android) AnalyticsHub app key. Distinct from the web key.
    public const string ApiKey = "ah_4VJ7x7rjxpLtZgFu1YyuJVzeMX85rGbAISJcxQcSL0I";

#if DEBUG
    public const string Environment = "Dev";
#else
    public const string Environment = "Production";
#endif
}
