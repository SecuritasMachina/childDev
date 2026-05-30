using LevelUp.Data;
using LevelUp.Models;
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
            var db = new LocalDatabase(path, SqliteFixture.TestKey);
            await db.InitAsync();
            await db.Connection.InsertAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = "A", GoalText = "x", UpdatedOn = 1 });
            Assert.Equal(1, await db.Connection.Table<Goal>().CountAsync());
            await db.Connection.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task WrongKey_CannotRead()
    {
        var path = Path.Combine(Path.GetTempPath(), $"enc_{Guid.NewGuid():N}.db3");
        try
        {
            var good = new LocalDatabase(path, "Z29vZGtleWdvb2RrZXlnb29ka2V5Z29vZGtleTEy");
            await good.InitAsync();
            await good.Connection.InsertAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = "A", GoalText = "x", UpdatedOn = 1 });
            await good.Connection.CloseAsync();

            var bad = new LocalDatabase(path, "YmFka2V5YmFka2V5YmFka2V5YmFka2V5YmFka2V5MTI=");
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await bad.InitAsync();
                await bad.Connection.Table<Goal>().CountAsync();
            });
            await bad.Connection.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
