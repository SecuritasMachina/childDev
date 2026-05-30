namespace LevelUp.Services;

/// <summary>Supplies OS/device metadata for AnalyticsHub sessions, per platform.</summary>
public interface IDeviceMetadataProvider
{
    string Os { get; }
    string Device { get; }
}

#if ANDROID
public class DeviceMetadataProvider : IDeviceMetadataProvider
{
    public string Os => $"Android {Microsoft.Maui.Devices.DeviceInfo.Current.VersionString}";
    public string Device => Microsoft.Maui.Devices.DeviceInfo.Current.Model ?? "Android";
}
#elif IOS
// ── iOS placeholder ──────────────────────────────────────────────────────────
// The app does not yet produce an iOS build. When the net8.0-ios target ships,
// implement these with Microsoft.Maui.Devices.DeviceInfo (same as Android).
public class DeviceMetadataProvider : IDeviceMetadataProvider
{
    public string Os => "iOS";        // TODO(ios): real OS version
    public string Device => "iOS";    // TODO(ios): real device model
}
#else
// Non-platform target (net8.0 used for unit tests): no MAUI device APIs available.
public class DeviceMetadataProvider : IDeviceMetadataProvider
{
    public string Os => "Unknown";
    public string Device => "Unknown";
}
#endif
