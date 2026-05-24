using LevelUp.Data;
using LevelUp.Models;
using SQLite;

namespace LevelUp.Tests;

public class GoalProgressEdgeCaseTests : IDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private readonly GoalProgressRepository _repo;

    public GoalProgressEdgeCaseTests()
    {
        SqliteFixture.EnsureInit();
        _db = new SQLiteAsyncConnection(":memory:");
        _db.CreateTableAsync<GoalProgress>().GetAwaiter().GetResult();
        _repo = new GoalProgressRepository(_db);
    }

    public void Dispose() => _db.CloseAsync().GetAwaiter().GetResult();

    [Fact]
    public async Task SaveMultipleProgress_AllReturned_OrderedByUpdatedOnDesc()
    {
        var goalFk = System.Guid.NewGuid().ToString();
        var t1 = 1_000_000L;
        var t2 = 2_000_000L;
        var t3 = 3_000_000L;

        // Insert in shuffled order to confirm ordering is by timestamp, not insertion order
        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = "account1", GoalFk = goalFk, NextStepItems = "middle", UpdatedOn = t2 });
        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = "account1", GoalFk = goalFk, NextStepItems = "oldest", UpdatedOn = t1 });
        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = "account1", GoalFk = goalFk, NextStepItems = "newest", UpdatedOn = t3 });

        var items = await _repo.GetForGoalAsync(goalFk);

        Assert.Equal(3, items.Count);
        Assert.Equal("newest", items[0].NextStepItems);
        Assert.Equal("middle", items[1].NextStepItems);
        Assert.Equal("oldest", items[2].NextStepItems);
    }

    [Fact]
    public async Task SoftDelete_ProgressNote_NotReturnedInGet()
    {
        var goalFk = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Save an active note then soft-delete it (DeletedAt == UpdatedOn)
        var guid = System.Guid.NewGuid().ToString();
        await _repo.SaveAsync(new GoalProgress
        {
            Guid = guid,
            AccountFk = "account1",
            GoalFk = goalFk,
            NextStepItems = "steps to remove",
            UpdatedOn = now
        });

        // Soft-delete via UpsertFromSync (simulates server-sent tombstone)
        await _repo.UpsertFromSyncAsync(new GoalProgress
        {
            Guid = guid,
            AccountFk = "account1",
            GoalFk = goalFk,
            NextStepItems = null,
            UpdatedOn = now + 500,
            DeletedAt = now + 500
        });

        var items = await _repo.GetForGoalAsync(goalFk);

        Assert.Empty(items);
    }

    [Fact]
    public async Task UpsertFromSync_OlderTimestamp_NotIncludedInGetModifiedSince()
    {
        // The server sends down only the winning (newest) record via LWW.
        // If the server sends a record with an old timestamp, GetModifiedSince
        // with a threshold above that timestamp must not return it.
        var goalFk = System.Guid.NewGuid().ToString();
        var guid = System.Guid.NewGuid().ToString();
        var recentThreshold = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Upsert a record with a very old timestamp (simulates an old sync payload)
        await _repo.UpsertFromSyncAsync(new GoalProgress
        {
            Guid = guid,
            AccountFk = "account1",
            GoalFk = goalFk,
            NextStepItems = "old sync record",
            UpdatedOn = 1000L
        });

        // GetModifiedSince with a threshold above the record's timestamp excludes it
        var modified = await _repo.GetModifiedSinceAsync("account1", recentThreshold);

        Assert.DoesNotContain(modified, p => p.Guid == guid);
    }

    [Fact]
    public async Task Progress_IsolatedBetweenGoals()
    {
        var goalA = System.Guid.NewGuid().ToString();
        var goalB = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _repo.SaveAsync(new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            GoalFk = goalA,
            NextStepItems = "goal A step",
            UpdatedOn = now
        });
        await _repo.SaveAsync(new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            GoalFk = goalB,
            NextStepItems = "goal B step",
            UpdatedOn = now
        });

        var itemsA = await _repo.GetForGoalAsync(goalA);
        var itemsB = await _repo.GetForGoalAsync(goalB);

        Assert.Single(itemsA);
        Assert.Equal("goal A step", itemsA[0].NextStepItems);
        Assert.Single(itemsB);
        Assert.Equal("goal B step", itemsB[0].NextStepItems);
        Assert.DoesNotContain(itemsA, i => i.NextStepItems == "goal B step");
        Assert.DoesNotContain(itemsB, i => i.NextStepItems == "goal A step");
    }

    [Fact]
    public async Task Progress_DeletedAt_ExcludedEvenWithNewerTimestamp()
    {
        var goalFk = System.Guid.NewGuid().ToString();
        var guid = System.Guid.NewGuid().ToString();

        // Insert soft-deleted record with a very high timestamp
        var veryHighTs = long.MaxValue / 2;
        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = guid,
            AccountFk = "account1",
            GoalFk = goalFk,
            NextStepItems = "should not appear",
            UpdatedOn = veryHighTs,
            DeletedAt = veryHighTs
        });

        // Also insert a regular active note with a much lower timestamp
        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            GoalFk = goalFk,
            NextStepItems = "active step",
            UpdatedOn = 1000L
        });

        var items = await _repo.GetForGoalAsync(goalFk);

        // Soft-deleted must be excluded regardless of its high UpdatedOn
        Assert.Single(items);
        Assert.Equal("active step", items[0].NextStepItems);
        Assert.DoesNotContain(items, i => i.Guid == guid);
    }
}
