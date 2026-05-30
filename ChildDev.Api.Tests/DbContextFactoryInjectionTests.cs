using ChildDev.Api.Data;
using ChildDev.Api.Models.Entities;
using ChildDev.Api.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChildDev.Api.Tests;

public class DbContextFactoryInjectionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task DbContextFactory_CanCreateContext_AndRoundTripsEncryptedData()
    {
        using var scope = factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        // Must not throw — proves factory resolves EncryptionService + ICurrentAccountProvider.
        await using var ctx = contextFactory.CreateDbContext();

        const string testAccount = "test-account-factory-gate";
        ctx.AccountGuid = testAccount;

        var goalGuid = Guid.NewGuid().ToString();
        var goal = new Goal
        {
            Guid = goalGuid,
            AccountFk = testAccount,
            GoalText = "Factory injection test goal",
            MeasurableOutcome = "Round-trip passes",
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        ctx.Goals.Add(goal);
        await ctx.SaveChangesAsync();

        // Read back through filter — AccountGuid is set to testAccount, filter should pass.
        var found = await ctx.Goals.FirstOrDefaultAsync(g => g.Guid == goalGuid);
        Assert.NotNull(found);
        Assert.Equal("Factory injection test goal", found.GoalText);
        Assert.Equal("Round-trip passes", found.MeasurableOutcome);
    }
}
