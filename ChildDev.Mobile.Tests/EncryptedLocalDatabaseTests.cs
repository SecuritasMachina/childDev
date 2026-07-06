using LevelUp.Data;
using LevelUp.Models;
using LevelUp.Services;
using Xunit;

namespace LevelUp.Tests;

public class EncryptedLocalDatabaseTests
{
    public EncryptedLocalDatabaseTests() => SqliteFixture.EnsureInit();

    [Fact]
    public async Task DataRoundTrips_WithKey()
    {
        var path = Path.Combine(Path.GetTempPath(), $"enc_{Guid.NewGuid():N}.db3");
        try
        {
            var db = new LocalDatabase(path, new FixedKeyDbKeyProvider(SqliteFixture.TestKey));
            await db.InitAsync();
            await db.Connection.InsertAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = "A", GoalText = "x", UpdatedOn = 1 });
            Assert.Equal(1, await db.Connection.Table<Goal>().CountAsync());
            await db.Connection.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task WrongKey_CannotReadPriorData_AndDoesNotBrick()
    {
        var path = Path.Combine(Path.GetTempPath(), $"enc_{Guid.NewGuid():N}.db3");
        try
        {
            var good = new LocalDatabase(path, new FixedKeyDbKeyProvider("Z29vZGtleWdvb2RrZXlnb29ka2V5Z29vZGtleTEy"));
            await good.InitAsync();
            await good.Connection.InsertAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = "A", GoalText = "x", UpdatedOn = 1 });
            await good.Connection.CloseAsync();

            // Opening with a DIFFERENT key must not expose the prior data and must not brick the app.
            // LocalDatabase runs DbMigrationGuard, which treats an unreadable (non-plaintext) DB as a
            // lost/rotated key and wipes it, so init succeeds against a fresh, empty DB — the wrong key
            // never reads the good-key rows.
            var bad = new LocalDatabase(path, new FixedKeyDbKeyProvider("YmFka2V5YmFka2V5YmFka2V5YmFka2V5YmFka2V5MTI="));
            await bad.InitAsync();
            Assert.Equal(0, await bad.Connection.Table<Goal>().CountAsync());
            await bad.Connection.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // Regression guard for the .NET 9 splash-hang fix: the per-device key must be fetched LAZILY inside
    // InitAsync (off the UI thread), never at construction. Constructing LocalDatabase on the UI thread
    // and then blocking on the key (SecureStorage/Keystore) is what deadlocked the app on the splash.
    [Fact]
    public async Task Key_IsFetchedLazily_NotAtConstruction()
    {
        var probe = new CountingKeyProvider("Z29vZGtleWdvb2RrZXlnb29ka2V5Z29vZGtleTEy");
        var path = Path.Combine(Path.GetTempPath(), $"enc_{Guid.NewGuid():N}.db3");
        try
        {
            var db = new LocalDatabase(path, probe);
            Assert.Equal(0, probe.Calls);                                        // no key access at construction
            Assert.Throws<InvalidOperationException>(() => db.Connection);       // unusable before init

            await db.InitAsync();
            Assert.True(probe.Calls >= 1);                                       // key fetched during async init

            await db.Connection.InsertAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = "A", GoalText = "x", UpdatedOn = 1 });
            Assert.Equal(1, await db.Connection.Table<Goal>().CountAsync());
            await db.Connection.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private sealed class CountingKeyProvider : IDbKeyProvider
    {
        private readonly string _key;
        public int Calls { get; private set; }
        public CountingKeyProvider(string key) => _key = key;
        public Task<string> GetKeyAsync()
        {
            Calls++;
            return Task.FromResult(_key);
        }
    }
}
