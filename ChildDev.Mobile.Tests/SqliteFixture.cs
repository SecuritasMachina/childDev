namespace LevelUp.Tests;

public static class SqliteFixture
{
    private static readonly object _initLock = new();
    private static bool _initialized = false;

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
