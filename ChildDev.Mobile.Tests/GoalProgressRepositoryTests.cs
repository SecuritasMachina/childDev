using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using SQLite;

namespace ChildDev.Mobile.Tests;

public class GoalProgressRepositoryTests : IDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private readonly GoalProgressRepository _repo;

    public GoalProgressRepositoryTests()
    {
        SqliteFixture.EnsureInit();
        _db = new SQLiteAsyncConnection(":memory:");
        _db.CreateTableAsync<GoalProgress>().GetAwaiter().GetResult();
        _repo = new GoalProgressRepository(_db);
    }

    public void Dispose() => _db.CloseAsync().GetAwaiter().GetResult();

    [Fact]
    public async Task Save_NewProgress_CanBeRetrievedForGoal()
    {
        var goalFk = System.Guid.NewGuid().ToString();
        var progress = new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            GoalFk = goalFk,
            NextStepItems = "Step 1\nStep 2",
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _repo.SaveAsync(progress);
        var items = await _repo.GetForGoalAsync(goalFk);

        Assert.Single(items);
        Assert.Equal("Step 1\nStep 2", items[0].NextStepItems);
    }

    [Fact]
    public async Task GetForGoalAsync_ExcludesSoftDeleted()
    {
        var goalFk = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var active = new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = "account1", GoalFk = goalFk, NextStepItems = "active", UpdatedOn = now };
        var deleted = new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = "account1", GoalFk = goalFk, NextStepItems = "deleted", UpdatedOn = now, DeletedAt = now };

        await _repo.SaveAsync(active);
        await _db.InsertOrReplaceAsync(deleted);
        var items = await _repo.GetForGoalAsync(goalFk);

        Assert.Single(items);
        Assert.Equal("active", items[0].NextStepItems);
    }

    [Fact]
    public async Task GetForGoalAsync_OnlyReturnsMatchingGoal()
    {
        var goalA = System.Guid.NewGuid().ToString();
        var goalB = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _repo.SaveAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = "account1", GoalFk = goalA, NextStepItems = "A step", UpdatedOn = now });
        await _repo.SaveAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = "account1", GoalFk = goalB, NextStepItems = "B step", UpdatedOn = now });

        var items = await _repo.GetForGoalAsync(goalA);

        Assert.Single(items);
        Assert.Equal("A step", items[0].NextStepItems);
    }

    [Fact]
    public async Task GetLatestNextStepsAsync_ReturnsLatestPerGoal()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var goalFk = System.Guid.NewGuid().ToString();
        var tOld = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeMilliseconds();
        var tNew = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalFk = goalFk, NextStepItems = "old step", UpdatedOn = tOld });
        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalFk = goalFk, NextStepItems = "new step", UpdatedOn = tNew });

        var latest = await _repo.GetLatestNextStepsAsync(accountId);

        Assert.True(latest.ContainsKey(goalFk));
        Assert.Equal("new step", latest[goalFk]);
    }

    [Fact]
    public async Task GetLatestNextStepsAsync_ExcludesSoftDeleted()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var goalFk = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalFk = goalFk, NextStepItems = "deleted", UpdatedOn = now, DeletedAt = now });

        var latest = await _repo.GetLatestNextStepsAsync(accountId);

        Assert.False(latest.ContainsKey(goalFk));
    }

    [Fact]
    public async Task GetModifiedSince_ReturnsOnlyNewerRecords()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var tOld = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        var tNew = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalFk = "g1", NextStepItems = "old", UpdatedOn = tOld });
        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalFk = "g1", NextStepItems = "new", UpdatedOn = tNew });

        var modified = await _repo.GetModifiedSinceAsync(accountId, tOld);

        Assert.Single(modified);
        Assert.Equal("new", modified[0].NextStepItems);
    }

    [Fact]
    public async Task DeleteForGoal_SoftDeletesAllProgressForThatGoal()
    {
        var goalFk = System.Guid.NewGuid().ToString();
        var otherGoalFk = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _repo.SaveAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = "account1", GoalFk = goalFk, NextStepItems = "step A", UpdatedOn = now });
        await _repo.SaveAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = "account1", GoalFk = goalFk, NextStepItems = "step B", UpdatedOn = now });
        await _repo.SaveAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = "account1", GoalFk = otherGoalFk, NextStepItems = "other", UpdatedOn = now });

        await _repo.DeleteForGoalAsync(goalFk);

        var deletedItems = await _repo.GetForGoalAsync(goalFk);
        var otherItems = await _repo.GetForGoalAsync(otherGoalFk);
        Assert.Empty(deletedItems);
        Assert.Single(otherItems);
    }

    [Fact]
    public async Task DeleteForGoal_SetsUpdatedOnToDeletedAt()
    {
        var goalFk = System.Guid.NewGuid().ToString();
        var guid1 = System.Guid.NewGuid().ToString();
        var guid2 = System.Guid.NewGuid().ToString();

        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = guid1, AccountFk = "account1", GoalFk = goalFk, NextStepItems = "step A", UpdatedOn = 1000L });
        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = guid2, AccountFk = "account1", GoalFk = goalFk, NextStepItems = "step B", UpdatedOn = 1000L });

        await _repo.DeleteForGoalAsync(goalFk);

        var r1 = await _db.FindAsync<GoalProgress>(guid1);
        var r2 = await _db.FindAsync<GoalProgress>(guid2);
        Assert.NotNull(r1!.DeletedAt);
        Assert.Equal(r1.DeletedAt!.Value, r1.UpdatedOn);
        Assert.True(r1.UpdatedOn > 1000L);
        Assert.NotNull(r2!.DeletedAt);
        Assert.Equal(r2.DeletedAt!.Value, r2.UpdatedOn);
    }

    [Fact]
    public async Task UpsertFromSync_OverwritesExistingRecord()
    {
        var guid = System.Guid.NewGuid().ToString();
        var goalFk = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _repo.SaveAsync(new GoalProgress { Guid = guid, AccountFk = "account1", GoalFk = goalFk, NextStepItems = "original", UpdatedOn = now });
        await _repo.UpsertFromSyncAsync(new GoalProgress { Guid = guid, AccountFk = "account1", GoalFk = goalFk, NextStepItems = "synced", UpdatedOn = now + 1000 });

        var items = await _repo.GetForGoalAsync(goalFk);
        Assert.Single(items);
        Assert.Equal("synced", items[0].NextStepItems);
    }

    [Fact]
    public async Task GetModifiedSinceAsync_ExcludesOtherAccounts()
    {
        var account1 = System.Guid.NewGuid().ToString();
        var account2 = System.Guid.NewGuid().ToString();
        var tNew = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = account1, GoalFk = "g1", NextStepItems = "mine", UpdatedOn = tNew });
        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = account2, GoalFk = "g2", NextStepItems = "theirs", UpdatedOn = tNew });

        var results = await _repo.GetModifiedSinceAsync(account1, 0);

        Assert.Single(results);
        Assert.Equal("mine", results[0].NextStepItems);
    }
}
