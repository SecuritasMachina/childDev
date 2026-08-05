using System.Reflection;

namespace LevelUp;

public static class BuildInfo
{
    // Stamped by the AssemblyMetadata "BuildTimestamp" item in LevelUp.csproj at build time.
    // The release container sets TZ=America/New_York so the "ET" label is truthful there;
    // never replace this with a hardcoded literal — it silently goes stale.
    public static string BuildTimestamp { get; } =
        typeof(BuildInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildTimestamp")?.Value ?? "unknown";
}
