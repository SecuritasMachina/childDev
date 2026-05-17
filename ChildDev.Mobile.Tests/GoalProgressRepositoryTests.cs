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
    public async Task GetForGoalAsync_ThreeItems_OrderedByUpdatedOnDescending()
    {
        var goalFk = System.Guid.NewGuid().ToString();
        var oldest = DateTimeOffset.UtcNow.AddDays(-5).ToUnixTimeMilliseconds();
        var middle = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds();
        var newest = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Insert in shuffled order to confirm ORDER BY handles it
        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = "account1", GoalFk = goalFk, NextStepItems = "middle", UpdatedOn = middle });
        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = "account1", GoalFk = goalFk, NextStepItems = "oldest", UpdatedOn = oldest });
        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = "account1", GoalFk = goalFk, NextStepItems = "newest", UpdatedOn = newest });

        var items = await _repo.GetForGoalAsync(goalFk);

        Assert.Equal(3, items.Count);
        Assert.Equal("newest", items[0].NextStepItems);
        Assert.Equal("middle", items[1].NextStepItems);
        Assert.Equal("oldest", items[2].NextStepItems);
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
    public async Task GetModifiedSinceAsync_ExcludesRecordsWithZeroUpdatedOn()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var goalFk = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalFk = goalFk, NextStepItems = "zero", UpdatedOn = 0 });
        await _db.InsertOrReplaceAsync(new GoalProgress { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalFk = goalFk, NextStepItems = "normal", UpdatedOn = now });

        var results = await _repo.GetModifiedSinceAsync(accountId, 0);

        Assert.Single(results);
        Assert.Equal("normal", results[0].NextStepItems);
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

    [Fact]
    public async Task DeleteForGoalAsync_RecordsAppearInGetModifiedSince()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var goalFk = System.Guid.NewGuid().ToString();
        var oldTs = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalFk = goalFk,
            NextStepItems = "Step A", UpdatedOn = oldTs
        });
        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalFk = goalFk,
            NextStepItems = "Step B", UpdatedOn = oldTs
        });

        await _repo.DeleteForGoalAsync(goalFk);

        var modified = await _repo.GetModifiedSinceAsync(accountId, oldTs);
        Assert.Equal(2, modified.Count);
        Assert.All(modified, p => Assert.NotNull(p.DeletedAt));
    }

    [Fact]
    public async Task GetLatestProgressInfoAsync_ReturnsLatestPerGoal()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var goalFk = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalFk = goalFk,
            NextStepItems = "older", UpdatedOn = now - 5000
        });
        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalFk = goalFk,
            NextStepItems = "latest", UpdatedOn = now - 1000
        });

        var info = await _repo.GetLatestProgressInfoAsync(accountId);

        Assert.True(info.ContainsKey(goalFk));
        Assert.Equal("latest", info[goalFk].Steps);
        Assert.Equal(now - 1000, info[goalFk].UpdatedOn);
    }

    [Fact]
    public async Task GetLatestProgressInfoAsync_ExcludesDeletedEntries()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var goalFk = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalFk = goalFk,
            NextStepItems = "deleted", UpdatedOn = now, DeletedAt = now
        });

        var info = await _repo.GetLatestProgressInfoAsync(accountId);

        Assert.False(info.ContainsKey(goalFk));
    }

    [Fact]
    public async Task GetLatestProgressInfoAsync_WhenNoEntries_ReturnsEmptyDictionary()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var info = await _repo.GetLatestProgressInfoAsync(accountId);
        Assert.Empty(info);
    }

    [Fact]
    public async Task GetLatestProgressInfoAsync_ExcludesOtherAccounts()
    {
        var myAccount = System.Guid.NewGuid().ToString();
        var otherAccount = System.Guid.NewGuid().ToString();
        var goalFk = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new GoalProgress
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = otherAccount, GoalFk = goalFk,
            NextStepItems = "other", UpdatedOn = now
        });

        var info = await _repo.GetLatestProgressInfoAsync(myAccount);

        Assert.False(info.ContainsKey(goalFk));
    }
}
