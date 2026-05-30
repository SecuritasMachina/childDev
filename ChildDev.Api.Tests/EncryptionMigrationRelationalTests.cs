using ChildDev.Api.Data;
using ChildDev.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChildDev.Api.Tests;

/// <summary>
/// Exercises the production-critical path of <see cref="EncryptionMigrationHostedService"/> against a
/// real relational provider (SQLite in-memory), where raw legacy plaintext can be inserted and the
/// raw stored column can be inspected — something the EF InMemory provider cannot do.
/// </summary>
public class EncryptionMigrationRelationalTests
{
    private static readonly string Key = Convert.ToBase64String(new byte[32]);
    private sealed class StubAccounts(string? a) : ICurrentAccountProvider { public string? GetAccountGuid() => a; }

    private static AppDbContext Ctx(SqliteConnection conn) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options,
            new EncryptionService(Key), new StubAccounts("ACC-A"));

    [Fact]
    public async Task Migration_EncryptsLegacyPlaintext_AndIsIdempotent()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        try
        {
            using (var create = Ctx(conn))
                await create.Database.EnsureCreatedAsync();

            var guid = Guid.NewGuid().ToString();

            // Insert a legacy plaintext row by writing the raw column value directly (bypasses the converter).
            using (var raw = Ctx(conn))
                await raw.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Goals (Guid, AccountFk, GoalText, EnteredDate, UpdatedOn, IsPinned) VALUES ({0}, 'ACC-A', 'plain diary', 5, 5, 0)",
                    guid);

            // First migration pass encrypts it.
            using (var mig = Ctx(conn))
                Assert.True(await EncryptionMigrationHostedService.RunOnceAsync(mig, default) >= 1);

            // Raw column is now ciphertext.
            using (var raw = Ctx(conn))
            {
                var stored = (await raw.Database
                    .SqlQueryRaw<string>("SELECT GoalText AS Value FROM Goals WHERE Guid = {0}", guid)
                    .ToListAsync()).First();
                Assert.StartsWith("v1:", stored);
            }

            // EF read decrypts back to the original plaintext, and UpdatedOn is preserved (LWW-safe).
            using (var r = Ctx(conn))
            {
                var g = await r.Goals.IgnoreQueryFilters().FirstAsync(x => x.Guid == guid);
                Assert.Equal("plain diary", g.GoalText);
                Assert.Equal(5, g.UpdatedOn);
            }

            // Idempotent: a second pass finds nothing left to encrypt.
            using (var mig2 = Ctx(conn))
                Assert.Equal(0, await EncryptionMigrationHostedService.RunOnceAsync(mig2, default));
        }
        finally
        {
            await conn.CloseAsync();
        }
    }
}
