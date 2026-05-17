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
    public async Task GetForGoalAsync_OrdersByUpdatedOnDescending()
    {
        var goalFk = System.Guid.NewGuid().ToString();
        var tOlder = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeMilliseconds();
        var tNewer = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = "account1", GoalFk = goalFk, NextStepItems = "older", UpdatedOn = tOlder });
        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = "account1", GoalFk = goalFk, NextStepItems = "newer", UpdatedOn = tNewer });

        var items = await _repo.GetForGoalAsync(goalFk);

        Assert.Equal(2, items.Count);
        Assert.Equal("newer", items[0].NextStepItems);
        Assert.Equal("older", items[1].NextStepItems);
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

    [Fact]
    public async Task GetModifiedSinceAsync_IncludesSoftDeletedRecords()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var goalFk = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalFk = goalFk,
            NextStepItems = "deleted step", UpdatedOn = now, DeletedAt = now
        });

        var modified = await _repo.GetModifiedSinceAsync(accountId, 0);
        Assert.Single(modified);
        Assert.NotNull(modified[0].DeletedAt);
    }

    [Fact]
    public async Task SaveAsync_Edit_BumpsUpdatedOn()
    {
        var guid = System.Guid.NewGuid().ToString();
        var goalFk = System.Guid.NewGuid().ToString();
        var progress = new GoalProgress
        {
            Guid = guid, AccountFk = "account1", GoalFk = goalFk,
            NextStepItems = "original", UpdatedOn = 1000L
        };
        await _db.InsertOrReplaceAsync(progress);

        progress.NextStepItems = "edited";
        await _repo.SaveAsync(progress);

        var items = await _repo.GetForGoalAsync(goalFk);
        Assert.Single(items);
        Assert.Equal("edited", items[0].NextStepItems);
        Assert.True(items[0].UpdatedOn > 1000L);
    }

    [Fact]
    public async Task SaveAsync_Edit_AppearsInGetModifiedSince()
    {
        var guid = System.Guid.NewGuid().ToString();
        var goalFk = System.Guid.NewGuid().ToString();
        var accountId = System.Guid.NewGuid().ToString();
        var oldTs = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        var progress = new GoalProgress
        {
            Guid = guid, AccountFk = accountId, GoalFk = goalFk,
            NextStepItems = "original", UpdatedOn = oldTs
        };
        await _db.InsertOrReplaceAsync(progress);

        progress.NextStepItems = "edited";
        await _repo.SaveAsync(progress);

        var modified = await _repo.GetModifiedSinceAsync(accountId, oldTs);
        Assert.Single(modified);
        Assert.Equal("edited", modified[0].NextStepItems);
    }

    [Fact]
    public async Task DeleteForGoalAsync_AlreadyDeletedRecordsAreNotRetouched()
    {
        var goalFk = System.Guid.NewGuid().ToString();
        var guid = System.Guid.NewGuid().ToString();
        var originalDeletedAt = 1000L;

        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = guid, AccountFk = "account1", GoalFk = goalFk,
            NextStepItems = "already gone", UpdatedOn = originalDeletedAt, DeletedAt = originalDeletedAt
        });

        await _repo.DeleteForGoalAsync(goalFk);

        var retrieved = await _db.FindAsync<GoalProgress>(guid);
        Assert.NotNull(retrieved);
        Assert.Equal(originalDeletedAt, retrieved!.UpdatedOn);
        Assert.Equal(originalDeletedAt, retrieved.DeletedAt!.Value);
    }

    [Fact]
    public async Task DeleteForGoalAsync_WhenNoActiveProgress_DoesNothing()
    {
        var emptyGoalFk = System.Guid.NewGuid().ToString();
        await _repo.DeleteForGoalAsync(emptyGoalFk);
        var items = await _repo.GetForGoalAsync(emptyGoalFk);
        Assert.Empty(items);
    }

    [Fact]
    public async Task GetLatestNextStepsAsync_ExcludesOtherAccounts()
    {
        var account1 = System.Guid.NewGuid().ToString();
        var account2 = System.Guid.NewGuid().ToString();
        var goalFk = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = account1, GoalFk = goalFk, NextStepItems = "mine", UpdatedOn = now });
        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = account2, GoalFk = goalFk, NextStepItems = "theirs", UpdatedOn = now + 1 });

        var result = await _repo.GetLatestNextStepsAsync(account1);

        Assert.True(result.ContainsKey(goalFk));
        Assert.Equal("mine", result[goalFk]);
    }

    [Fact]
    public async Task UpsertFromSyncAsync_PreservesServerTimestamp()
    {
        var guid = System.Guid.NewGuid().ToString();
        var goalFk = System.Guid.NewGuid().ToString();
        var serverTs = 12345678L;

        await _repo.UpsertFromSyncAsync(new GoalProgress
        {
            Guid = guid, AccountFk = "account1", GoalFk = goalFk,
            NextStepItems = "server step", UpdatedOn = serverTs
        });

        var retrieved = await _db.FindAsync<GoalProgress>(guid);
        Assert.NotNull(retrieved);
        Assert.Equal(serverTs, retrieved!.UpdatedOn);
    }

    [Fact]
    public async Task GetLatestNextStepsAsync_WhenLatestIsSoftDeleted_FallsBackToPrior()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var goalFk = System.Guid.NewGuid().ToString();
        var older = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        var newer = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId,
            GoalFk = goalFk, NextStepItems = "prior steps", UpdatedOn = older
        });
        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId,
            GoalFk = goalFk, NextStepItems = "deleted steps", UpdatedOn = newer, DeletedAt = newer
        });

        var latest = await _repo.GetLatestNextStepsAsync(accountId);

        Assert.True(latest.ContainsKey(goalFk));
        Assert.Equal("prior steps", latest[goalFk]);
    }

    [Fact]
    public async Task UpsertFromSyncAsync_PersistsAllOptionalFields()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = System.Guid.NewGuid().ToString();
        var meetingDate = now + 86400000L;

        await _repo.UpsertFromSyncAsync(new GoalProgress
        {
            Guid = guid, AccountFk = "account1",
            GoalFk = System.Guid.NewGuid().ToString(),
            NextStepItems = "Synced: step A, step B",
            NextMeetingDate = meetingDate,
            UpdatedOn = now
        });

        var retrieved = await _db.FindAsync<GoalProgress>(guid);
        Assert.NotNull(retrieved);
        Assert.Equal("Synced: step A, step B", retrieved!.NextStepItems);
        Assert.Equal(meetingDate, retrieved.NextMeetingDate);
    }

    [Fact]
    public async Task SaveAsync_PersistsAllOptionalFields()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var meetingDate = now + 86400000L;
        var progress = new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            GoalFk = System.Guid.NewGuid().ToString(),
            NextStepItems = "Step 1: plan, Step 2: execute",
            NextMeetingDate = meetingDate,
            UpdatedOn = now
        };

        await _repo.SaveAsync(progress);
        var retrieved = await _db.FindAsync<GoalProgress>(progress.Guid);

        Assert.NotNull(retrieved);
        Assert.Equal("Step 1: plan, Step 2: execute", retrieved!.NextStepItems);
        Assert.Equal(meetingDate, retrieved.NextMeetingDate);
    }
}
