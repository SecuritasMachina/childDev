namespace LevelUp.Tests;

public static class SqliteFixture
{
    private static readonly object _initLock = new();
    private static bool _initialized = false;

    /// <summary>
    /// A stable 32-byte base64 key for use in encryption tests.
    /// Decodes to "TestKeyTestKeyTestKeyTestKey1234" (32 bytes).
    /// </summary>
    public const string TestKey = "VGVzdEtleVRlc3RLZXlUZXN0S2V5VGVzdEtleTEyMzQ=";

    public static void EnsureInit()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            SQLitePCL.Batteries_V2.Init();
            _initialized = true;
        }
    }
}
