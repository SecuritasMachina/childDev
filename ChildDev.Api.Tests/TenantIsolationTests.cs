using ChildDev.Api.Data;
using ChildDev.Api.Models.Entities;
using ChildDev.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Tests;

public class TenantIsolationTests
{
    private static readonly string Key = Convert.ToBase64String(new byte[32]);

    // Builds a context bound to a fixed account, sharing one in-memory store via dbName.
    private static AppDbContext Ctx(string dbName, string? account)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var enc = new EncryptionService(Key);
        var accounts = new StubAccounts(account);
        return new AppDbContext(opts, enc, accounts);
    }

    private sealed class StubAccounts(string? acc) : ICurrentAccountProvider
    {
        public string? GetAccountGuid() => acc;
    }

    [Fact]
    public async Task QueryFilter_ScopesGoalsToCurrentAccount()
    {
        var db = $"iso_{Guid.NewGuid():N}";
        using (var seed = Ctx(db, "ACC-A"))
        {
            // seed for A and B (seed context account doesn't matter for Add)
            seed.Goals.Add(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = "ACC-A", GoalText = "A secret", EnteredDate = 1, UpdatedOn = 1 });
            seed.Goals.Add(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = "ACC-B", GoalText = "B secret", EnteredDate = 1, UpdatedOn = 1 });
            await seed.SaveChangesAsync();
        }
        using var ctxA = Ctx(db, "ACC-A");
        var goals = await ctxA.Goals.ToListAsync();
        Assert.NotEmpty(goals);
        Assert.All(goals, g => Assert.Equal("ACC-A", g.AccountFk));
        Assert.DoesNotContain(goals, g => g.AccountFk == "ACC-B");
    }

    [Fact]
    public async Task NullAccount_SeesNothing()
    {
        var db = $"iso_{Guid.NewGuid():N}";
        using (var seed = Ctx(db, "ACC-A"))
        {
            seed.Goals.Add(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = "ACC-A", GoalText = "x", EnteredDate = 1, UpdatedOn = 1 });
            await seed.SaveChangesAsync();
        }
        using var anon = Ctx(db, null);
        Assert.Empty(await anon.Goals.ToListAsync());
    }

    [Fact]
    public async Task EncryptedColumn_RoundTripsTransparently()
    {
        var db = $"iso_{Guid.NewGuid():N}";
        var guid = Guid.NewGuid().ToString();
        using (var w = Ctx(db, "ACC-C"))
        {
            w.Goals.Add(new Goal { Guid = guid, AccountFk = "ACC-C", GoalText = "diary entry", EnteredDate = 1, UpdatedOn = 1 });
            await w.SaveChangesAsync();
        }
        using var r = Ctx(db, "ACC-C");
        var g = await r.Goals.FirstAsync(x => x.Guid == guid);
        Assert.Equal("diary entry", g.GoalText);
    }

    [Fact]
    public async Task Account_IsNotFiltered_LookupByNickNameWorks()
    {
        var db = $"iso_{Guid.NewGuid():N}";
        using (var seed = Ctx(db, "ACC-A"))
        {
            seed.Accounts.Add(new Account { Guid = "ACC-A", NickName = "kid1", PinHash = "h", CreatedOn = 1 });
            await seed.SaveChangesAsync();
        }
        // a context scoped to a DIFFERENT account must still find the Account by NickName
        using var other = Ctx(db, "ACC-Z");
        var acc = await other.Accounts.FirstOrDefaultAsync(a => a.NickName == "kid1");
        Assert.NotNull(acc);
    }
}
