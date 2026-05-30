using LevelUp.Data;
using LevelUp.Models;
using SQLite;
using Xunit;

namespace LevelUp.Tests;

public class DbMigrationGuardTests
{
    public DbMigrationGuardTests() => SqliteFixture.EnsureInit();

    [Fact]
    public void Migrate_PreservesIdentityCredentialsAndData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"legacy_{Guid.NewGuid():N}.db3");
        try
        {
            // Seed a LEGACY PLAINTEXT db (no key) with an Account carrying identity + a Goal.
            using (var plain = new SQLiteConnection(new SQLiteConnectionString(path, storeDateTimeAsTicks: true, key: null)))
            {
                plain.CreateTable<Account>();
                plain.CreateTable<Goal>();
                plain.Insert(new Account { Guid = "srv-123", NickName = "kid", PinHash = "hash", CreatedOn = 1, LastSyncAt = 999, ServerJwt = "jwt-abc", ServerUrl = "https://srv" });
                plain.Insert(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = "srv-123", GoalText = "diary", UpdatedOn = 5 });
            }

            var outcome = DbMigrationGuard.EnsureEncrypted(path, SqliteFixture.TestKey);
            Assert.Equal(DbMigrationOutcome.Migrated, outcome);

            // Encrypted DB opens with the key and ALL identity + data survived.
            using (var enc = new SQLiteConnection(new SQLiteConnectionString(path, storeDateTimeAsTicks: true, key: SqliteFixture.TestKey)))
            {
                var acc = enc.Table<Account>().First();
                Assert.Equal("srv-123", acc.Guid);
                Assert.Equal("jwt-abc", acc.ServerJwt);
                Assert.Equal("https://srv", acc.ServerUrl);
                Assert.Equal(999, acc.LastSyncAt);
                Assert.Equal("hash", acc.PinHash);
                Assert.Equal(1, enc.Table<Goal>().Count());
            }
        }
        finally { foreach (var f in new[]{path, path+"-wal", path+"-shm", path+".enc-migrate"}) if (File.Exists(f)) File.Delete(f); }
    }

    [Fact]
    public void Migrate_ResultRejectsWrongKey()
    {
        var path = Path.Combine(Path.GetTempPath(), $"legacy_{Guid.NewGuid():N}.db3");
        try
        {
            using (var plain = new SQLiteConnection(new SQLiteConnectionString(path, storeDateTimeAsTicks: true, key: null)))
            {
                plain.CreateTable<Account>();
                plain.Insert(new Account { Guid = "g", NickName = "n", PinHash = "h", CreatedOn = 1 });
            }
            Assert.Equal(DbMigrationOutcome.Migrated, DbMigrationGuard.EnsureEncrypted(path, SqliteFixture.TestKey));

            var wrong = "YmFka2V5YmFka2V5YmFka2V5YmFka2V5YmFka2V5MTI=";
            Assert.ThrowsAny<Exception>(() =>
            {
                using var bad = new SQLiteConnection(new SQLiteConnectionString(path, storeDateTimeAsTicks: true, key: wrong));
                bad.Table<Account>().Count();
            });
        }
        finally { foreach (var f in new[]{path, path+"-wal", path+"-shm", path+".enc-migrate"}) if (File.Exists(f)) File.Delete(f); }
    }

    [Fact]
    public void AlreadyEncrypted_IsNoOp()
    {
        var path = Path.Combine(Path.GetTempPath(), $"enc_{Guid.NewGuid():N}.db3");
        try
        {
            using (var enc = new SQLiteConnection(new SQLiteConnectionString(path, storeDateTimeAsTicks: true, key: SqliteFixture.TestKey)))
            { enc.CreateTable<Account>(); enc.Insert(new Account { Guid="g", NickName="n", PinHash="h", CreatedOn=1 }); }

            Assert.Equal(DbMigrationOutcome.AlreadyEncrypted, DbMigrationGuard.EnsureEncrypted(path, SqliteFixture.TestKey));
            using var check = new SQLiteConnection(new SQLiteConnectionString(path, storeDateTimeAsTicks: true, key: SqliteFixture.TestKey));
            Assert.Equal(1, check.Table<Account>().Count());
        }
        finally { foreach (var f in new[]{path, path+"-wal", path+"-shm"}) if (File.Exists(f)) File.Delete(f); }
    }

    [Fact]
    public void CorruptFile_FallsBackToWipe()
    {
        var path = Path.Combine(Path.GetTempPath(), $"corrupt_{Guid.NewGuid():N}.db3");
        File.WriteAllText(path, "this is not a database header at all");
        try
        {
            var outcome = DbMigrationGuard.EnsureEncrypted(path, SqliteFixture.TestKey);
            Assert.Equal(DbMigrationOutcome.Wiped, outcome);
            // After wipe the path should not exist (a fresh encrypted DB will be created by LocalDatabase later).
            Assert.False(File.Exists(path));
        }
        finally { foreach (var f in new[]{path, path+"-wal", path+"-shm", path+".enc-migrate"}) if (File.Exists(f)) File.Delete(f); }
    }
}
