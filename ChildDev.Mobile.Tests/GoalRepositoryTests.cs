using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using SQLite;

namespace ChildDev.Mobile.Tests;

public class GoalRepositoryTests : IDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private readonly GoalRepository _repo;

    public GoalRepositoryTests()
    {
        SqliteFixture.EnsureInit();
        _db = new SQLiteAsyncConnection(":memory:");
        _db.CreateTableAsync<Goal>().GetAwaiter().GetResult();
        _repo = new GoalRepository(_db);
    }

    public void Dispose() => _db.CloseAsync().GetAwaiter().GetResult();

    [Fact]
    public async Task Save_NewGoal_CanBeRetrieved()
    {
        var goal = new Goal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            GoalText = "Learn piano",
            EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _repo.SaveAsync(goal);
        var all = await _repo.GetAllActiveAsync("account1");

        Assert.Single(all);
        Assert.Equal("Learn piano", all[0].GoalText);
    }

    [Fact]
    public async Task GetAllActiveAsync_ExcludesOtherAccounts()
    {
        var account1 = System.Guid.NewGuid().ToString();
        var account2 = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account1, GoalText = "my goal", EnteredDate = now, UpdatedOn = now });
        await _db.InsertOrReplaceAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account2, GoalText = "their goal", EnteredDate = now, UpdatedOn = now });

        var results = await _repo.GetAllActiveAsync(account1);

        Assert.Single(results);
        Assert.Equal("my goal", results[0].GoalText);
    }

    [Fact]
    public async Task GetAllActiveAsync_ActiveBeforeCompleted()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var active = new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = "account2",
            GoalText = "Active", EnteredDate = now - 1000, UpdatedOn = now };
        var completed = new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = "account2",
            GoalText = "Completed", EnteredDate = now, UpdatedOn = now, CompletionDate = now };

        await _repo.SaveAsync(active);
        await _repo.SaveAsync(completed);
        var all = await _repo.GetAllActiveAsync("account2");

        Assert.Equal(2, all.Count);
        Assert.Equal("Active", all[0].GoalText);
        Assert.Equal("Completed", all[1].GoalText);
    }

    [Fact]
    public async Task Delete_SoftDeletes_ExcludedFromActive()
    {
        var goal = new Goal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            GoalText = "Delete me",
            EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _repo.SaveAsync(goal);
        await _repo.DeleteAsync(goal.Guid);
        var all = await _repo.GetAllActiveAsync("account1");
        Assert.Empty(all);
        var retrieved = await _repo.GetAsync(goal.Guid);
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved!.DeletedAt);
        Assert.Equal(retrieved.DeletedAt!.Value, retrieved.UpdatedOn);
    }

    [Fact]
    public async Task CompleteAsync_SetsCompletionDate()
    {
        var goal = new Goal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            GoalText = "Complete me",
            EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _repo.SaveAsync(goal);
        await _repo.CompleteAsync(goal.Guid);

        var retrieved = await _repo.GetAsync(goal.Guid);
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.CompletionDate);
    }

    [Fact]
    public async Task CompleteAsync_SetsUpdatedOnToCompletionDate()
    {
        var guid = System.Guid.NewGuid().ToString();
        var goal = new Goal
        {
            Guid = guid,
            AccountFk = "account1",
            GoalText = "Sync after complete",
            EnteredDate = 1000L,
            UpdatedOn = 1000L
        };
        await _db.InsertOrReplaceAsync(goal);

        await _repo.CompleteAsync(guid);

        var retrieved = await _repo.GetAsync(guid);
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved!.CompletionDate);
        Assert.Equal(retrieved.CompletionDate!.Value, retrieved.UpdatedOn);
        Assert.True(retrieved.UpdatedOn > 1000L);
    }

    [Fact]
    public async Task GetModifiedSince_ReturnsOnlyNewerRecords()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var tOld = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        var tNew = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalText = "old", EnteredDate = tOld, UpdatedOn = tOld });
        await _db.InsertOrReplaceAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalText = "new", EnteredDate = tNew, UpdatedOn = tNew });

        var modified = await _repo.GetModifiedSinceAsync(accountId, tOld);
        Assert.Single(modified);
        Assert.Equal("new", modified[0].GoalText);
    }

    [Fact]
    public async Task UpsertFromSync_OverwritesExistingGoal()
    {
        var guid = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _repo.SaveAsync(new Goal { Guid = guid, AccountFk = "account1", GoalText = "original", EnteredDate = now, UpdatedOn = now });
        await _repo.UpsertFromSyncAsync(new Goal { Guid = guid, AccountFk = "account1", GoalText = "synced", EnteredDate = now, UpdatedOn = now + 1000 });

        var retrieved = await _repo.GetAsync(guid);
        Assert.NotNull(retrieved);
        Assert.Equal("synced", retrieved.GoalText);
    }

    [Fact]
    public async Task UpsertFromSyncAsync_PreservesOriginalEnteredDate_WhenServerSendsDifferentValue()
    {
        var guid = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var originalEnteredDate = now - 86_400_000L; // 1 day ago

        await _repo.SaveAsync(new Goal { Guid = guid, AccountFk = "account1", GoalText = "original",
            EnteredDate = originalEnteredDate, UpdatedOn = now });

        // Server sends same Guid with newer UpdatedOn but a different EnteredDate
        await _repo.UpsertFromSyncAsync(new Goal { Guid = guid, AccountFk = "account1", GoalText = "synced",
            EnteredDate = now, UpdatedOn = now + 1000 });

        var retrieved = await _repo.GetAsync(guid);
        Assert.NotNull(retrieved);
        Assert.Equal(originalEnteredDate, retrieved!.EnteredDate);
    }

    [Fact]
    public async Task SaveAsync_Edit_BumpsUpdatedOn()
    {
        var guid = System.Guid.NewGuid().ToString();
        var goal = new Goal
        {
            Guid = guid, AccountFk = "account1", GoalText = "original",
            EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedOn = 1000L
        };
        await _db.InsertOrReplaceAsync(goal);

        goal.GoalText = "edited";
        await _repo.SaveAsync(goal);

        var saved = await _repo.GetAsync(guid);
        Assert.NotNull(saved);
        Assert.Equal("edited", saved!.GoalText);
        Assert.True(saved.UpdatedOn > 1000L);
    }

    [Fact]
    public async Task SaveAsync_Edit_AppearsInGetModifiedSince()
    {
        var guid = System.Guid.NewGuid().ToString();
        var oldTs = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        var goal = new Goal
        {
            Guid = guid, AccountFk = "account4", GoalText = "original",
            EnteredDate = oldTs, UpdatedOn = oldTs
        };
        await _db.InsertOrReplaceAsync(goal);

        goal.GoalText = "edited";
        await _repo.SaveAsync(goal);

        var modified = await _repo.GetModifiedSinceAsync("account4", oldTs);
        Assert.Single(modified);
        Assert.Equal("edited", modified[0].GoalText);
    }

    [Fact]
    public async Task GetModifiedSinceAsync_ExcludesOtherAccounts()
    {
        var account1 = System.Guid.NewGuid().ToString();
        var account2 = System.Guid.NewGuid().ToString();
        var tNew = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account1, GoalText = "mine", EnteredDate = tNew, UpdatedOn = tNew });
        await _db.InsertOrReplaceAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account2, GoalText = "theirs", EnteredDate = tNew, UpdatedOn = tNew });

        var results = await _repo.GetModifiedSinceAsync(account1, 0);

        Assert.Single(results);
        Assert.Equal("mine", results[0].GoalText);
    }

    [Fact]
    public async Task GetModifiedSinceAsync_ExcludesRecordsWithZeroUpdatedOn()
    {
        // UpdatedOn = 0 is an invalid sentinel (means never modified). The strict-greater-than
        // comparison ensures such records are never uploaded, even on initial sync (since = 0).
        var accountId = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalText = "zero updatedOn", EnteredDate = now, UpdatedOn = 0 });
        await _db.InsertOrReplaceAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalText = "normal", EnteredDate = now, UpdatedOn = now });

        var results = await _repo.GetModifiedSinceAsync(accountId, 0);

        Assert.Single(results);
        Assert.Equal("normal", results[0].GoalText);
    }

    [Fact]
    public async Task GetAllActiveAsync_OrdersActiveGoalsByEnteredDateDescending()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var older = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds();
        var newer = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalText = "older goal", EnteredDate = older, UpdatedOn = older });
        await _db.InsertOrReplaceAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalText = "newer goal", EnteredDate = newer, UpdatedOn = newer });

        var results = await _repo.GetAllActiveAsync(accountId);

        Assert.Equal(2, results.Count);
        Assert.Equal("newer goal", results[0].GoalText);
        Assert.Equal("older goal", results[1].GoalText);
    }

    [Fact]
    public async Task GetAllActiveAsync_MultipleActiveGoals_OrderedByEnteredDateDescending()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var older = DateTimeOffset.UtcNow.AddDays(-5).ToUnixTimeMilliseconds();
        var middle = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds();
        var newer = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalText = "middle", EnteredDate = middle, UpdatedOn = middle });
        await _db.InsertOrReplaceAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalText = "older", EnteredDate = older, UpdatedOn = older });
        await _db.InsertOrReplaceAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalText = "newer", EnteredDate = newer, UpdatedOn = newer });

        var results = await _repo.GetAllActiveAsync(accountId);

        Assert.Equal(3, results.Count);
        Assert.Equal("newer", results[0].GoalText);
        Assert.Equal("middle", results[1].GoalText);
        Assert.Equal("older", results[2].GoalText);
    }

    [Fact]
    public async Task GetModifiedSinceAsync_IncludesSoftDeletedRecords()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId,
            GoalText = "deleted goal", EnteredDate = now, UpdatedOn = now, DeletedAt = now
        });

        var modified = await _repo.GetModifiedSinceAsync(accountId, 0);
        Assert.Single(modified);
        Assert.NotNull(modified[0].DeletedAt);
    }

    [Fact]
    public async Task UpsertFromSyncAsync_PreservesServerTimestamp()
    {
        var guid = System.Guid.NewGuid().ToString();
        var serverTs = 12345678L;

        await _repo.UpsertFromSyncAsync(new Goal
        {
            Guid = guid, AccountFk = "account1", GoalText = "server goal",
            EnteredDate = serverTs, UpdatedOn = serverTs
        });

        var retrieved = await _repo.GetAsync(guid);
        Assert.NotNull(retrieved);
        Assert.Equal(serverTs, retrieved!.UpdatedOn);
    }

    [Fact]
    public async Task DeleteAsync_WhenGuidNotFound_DoesNotThrow()
    {
        var nonExistentGuid = System.Guid.NewGuid().ToString();
        await _repo.DeleteAsync(nonExistentGuid);
        var retrieved = await _repo.GetAsync(nonExistentGuid);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task CompleteAsync_WhenGuidNotFound_DoesNotThrow()
    {
        var nonExistentGuid = System.Guid.NewGuid().ToString();
        await _repo.CompleteAsync(nonExistentGuid);
        var retrieved = await _repo.GetAsync(nonExistentGuid);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task GetAllActiveAsync_TwoCompletedGoals_OrderedByEnteredDateDescending()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var olderEntered = DateTimeOffset.UtcNow.AddDays(-5).ToUnixTimeMilliseconds();
        var newerEntered = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId,
            GoalText = "older completed", EnteredDate = olderEntered,
            CompletionDate = now, UpdatedOn = now
        });
        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId,
            GoalText = "newer completed", EnteredDate = newerEntered,
            CompletionDate = now, UpdatedOn = now
        });

        var results = await _repo.GetAllActiveAsync(accountId);

        Assert.Equal(2, results.Count);
        Assert.Equal("newer completed", results[0].GoalText);
        Assert.Equal("older completed", results[1].GoalText);
    }

    [Fact]
    public async Task UpsertFromSyncAsync_PersistsAllOptionalFields()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = System.Guid.NewGuid().ToString();
        var meetingDate = now + 86400000L;
        var expirationDate = now + 2592000000L;

        await _repo.UpsertFromSyncAsync(new Goal
        {
            Guid = guid, AccountFk = "account1", GoalText = "Synced goal",
            MeasurableOutcome = "Finish chapter 1",
            NextMeetingDate = meetingDate,
            ExpirationDate = expirationDate,
            EnteredDate = now, UpdatedOn = now
        });

        var retrieved = await _repo.GetAsync(guid);
        Assert.NotNull(retrieved);
        Assert.Equal("Finish chapter 1", retrieved!.MeasurableOutcome);
        Assert.Equal(meetingDate, retrieved.NextMeetingDate);
        Assert.Equal(expirationDate, retrieved.ExpirationDate);
    }

    [Fact]
    public async Task GetAsync_WhenGuidNotFound_ReturnsNull()
    {
        var result = await _repo.GetAsync(System.Guid.NewGuid().ToString());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllActiveAsync_CompletedThenDeletedGoal_IsExcluded()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // A goal that was completed then soft-deleted should be excluded (DeletedAt takes precedence)
        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = accountId,
            GoalText = "completed and deleted",
            EnteredDate = now,
            CompletionDate = now,
            UpdatedOn = now,
            DeletedAt = now
        });

        var all = await _repo.GetAllActiveAsync(accountId);
        Assert.Empty(all);
    }

    [Fact]
    public async Task GetAllActiveAsync_UpsertedSoftDeletedRecord_IsExcluded()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // A soft-deleted goal arriving via sync should not appear in GetAllActiveAsync
        await _repo.UpsertFromSyncAsync(new Goal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = accountId,
            GoalText = "synced deleted goal",
            EnteredDate = now,
            UpdatedOn = now,
            DeletedAt = now
        });

        var all = await _repo.GetAllActiveAsync(accountId);
        Assert.Empty(all);
    }

    [Fact]
    public async Task GetAllActiveAsync_MixedActiveAndCompletedGoals_CorrectOrdering()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var older = DateTimeOffset.UtcNow.AddDays(-5).ToUnixTimeMilliseconds();
        var newer = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Insert in shuffled order to confirm SQL ORDER BY handles it
        await _db.InsertOrReplaceAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalText = "older completed", EnteredDate = older, CompletionDate = now, UpdatedOn = now });
        await _db.InsertOrReplaceAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalText = "newer active", EnteredDate = newer, UpdatedOn = newer });
        await _db.InsertOrReplaceAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalText = "newer completed", EnteredDate = newer, CompletionDate = now, UpdatedOn = now });
        await _db.InsertOrReplaceAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, GoalText = "older active", EnteredDate = older, UpdatedOn = older });

        var results = await _repo.GetAllActiveAsync(accountId);

        Assert.Equal(4, results.Count);
        Assert.Equal("newer active", results[0].GoalText);
        Assert.Equal("older active", results[1].GoalText);
        Assert.Equal("newer completed", results[2].GoalText);
        Assert.Equal("older completed", results[3].GoalText);
    }

    [Fact]
    public async Task CompleteAsync_GoalAppearsInGetModifiedSince()
    {
        var guid = System.Guid.NewGuid().ToString();
        var oldTs = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = guid, AccountFk = "account1", GoalText = "to complete",
            EnteredDate = oldTs, UpdatedOn = oldTs
        });

        await _repo.CompleteAsync(guid);

        var modified = await _repo.GetModifiedSinceAsync("account1", oldTs);
        Assert.Single(modified);
        Assert.Equal(guid, modified[0].Guid);
        Assert.NotNull(modified[0].CompletionDate);
    }

    [Fact]
    public async Task DeleteAsync_GoalAppearsInGetModifiedSince()
    {
        var guid = System.Guid.NewGuid().ToString();
        var oldTs = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new Goal
        {
            Guid = guid, AccountFk = "account1", GoalText = "to delete",
            EnteredDate = oldTs, UpdatedOn = oldTs
        });

        await _repo.DeleteAsync(guid);

        var modified = await _repo.GetModifiedSinceAsync("account1", oldTs);
        Assert.Single(modified);
        Assert.Equal(guid, modified[0].Guid);
        Assert.NotNull(modified[0].DeletedAt);
    }

    [Fact]
    public async Task SaveAsync_PersistsAllOptionalFields()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            GoalText = "Run a marathon",
            MeasurableOutcome = "Complete 26.2 miles",
            NextMeetingDate = now + 86400000L,
            ExpirationDate = now + 2592000000L,
            EnteredDate = now,
            UpdatedOn = now
        };

        await _repo.SaveAsync(goal);
        var retrieved = await _repo.GetAsync(goal.Guid);

        Assert.NotNull(retrieved);
        Assert.Equal("Complete 26.2 miles", retrieved!.MeasurableOutcome);
        Assert.Equal(goal.NextMeetingDate, retrieved.NextMeetingDate);
        Assert.Equal(goal.ExpirationDate, retrieved.ExpirationDate);
    }

    [Fact]
    public async Task GetAsync_WhenDeleted_StillReturnsRecord()
    {
        var guid = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _repo.SaveAsync(new Goal
        {
            Guid = guid, AccountFk = "account1", GoalText = "to be deleted",
            EnteredDate = now, UpdatedOn = now
        });
        await _repo.DeleteAsync(guid);

        var retrieved = await _repo.GetAsync(guid);
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved!.DeletedAt);
    }
}
