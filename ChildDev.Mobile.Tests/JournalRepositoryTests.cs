using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using SQLite;

namespace ChildDev.Mobile.Tests;

public class JournalRepositoryTests : IDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private readonly JournalRepository _repo;

    public JournalRepositoryTests()
    {
        SqliteFixture.EnsureInit();
        _db = new SQLiteAsyncConnection(":memory:");
        _db.CreateTableAsync<Journal>().GetAwaiter().GetResult();
        _repo = new JournalRepository(_db);
    }

    public void Dispose() => _db.CloseAsync().GetAwaiter().GetResult();

    [Fact]
    public async Task Save_NewJournal_CanBeRetrieved()
    {
        var journal = new Journal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            Notes = "Today was good",
            EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _repo.SaveAsync(journal);
        var all = await _repo.GetAllActiveAsync("account1");

        Assert.Single(all);
        Assert.Equal("Today was good", all[0].Notes);
    }

    [Fact]
    public async Task Delete_SoftDeletes_ExcludedFromActive()
    {
        var journal = new Journal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            Notes = "To delete",
            EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _repo.SaveAsync(journal);
        await _repo.DeleteAsync(journal.Guid);

        var all = await _repo.GetAllActiveAsync("account1");
        Assert.Empty(all);
        var retrieved = await _repo.GetAsync(journal.Guid);
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved!.DeletedAt);
        Assert.Equal(retrieved.DeletedAt!.Value, retrieved.UpdatedOn);
    }

    [Fact]
    public async Task SaveAsync_Edit_BumpsUpdatedOn()
    {
        var guid = System.Guid.NewGuid().ToString();
        var journal = new Journal
        {
            Guid = guid, AccountFk = "account1", Notes = "original",
            EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedOn = 1000L
        };
        await _db.InsertOrReplaceAsync(journal);

        journal.Notes = "edited";
        await _repo.SaveAsync(journal);

        var saved = await _repo.GetAsync(guid);
        Assert.NotNull(saved);
        Assert.Equal("edited", saved!.Notes);
        Assert.True(saved.UpdatedOn > 1000L);
    }

    [Fact]
    public async Task SaveAsync_Edit_AppearsInGetModifiedSince()
    {
        var guid = System.Guid.NewGuid().ToString();
        var oldTs = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        var journal = new Journal
        {
            Guid = guid, AccountFk = "account3", Notes = "original",
            EnteredDate = oldTs, UpdatedOn = oldTs
        };
        await _db.InsertOrReplaceAsync(journal);

        journal.Notes = "edited";
        await _repo.SaveAsync(journal);

        var modified = await _repo.GetModifiedSinceAsync("account3", oldTs);
        Assert.Single(modified);
        Assert.Equal("edited", modified[0].Notes);
    }

    [Fact]
    public async Task GetModifiedSince_ReturnsOnlyNewerRecords()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var tOld = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        var tNew = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Journal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = accountId,
            Notes = "old",
            EnteredDate = tOld,
            UpdatedOn = tOld
        });
        await _db.InsertOrReplaceAsync(new Journal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = accountId,
            Notes = "new",
            EnteredDate = tNew,
            UpdatedOn = tNew
        });

        var modified = await _repo.GetModifiedSinceAsync(accountId, tOld);
        Assert.Single(modified);
        Assert.Equal("new", modified[0].Notes);
    }

    [Fact]
    public async Task UpsertFromSync_OverwritesExistingJournal()
    {
        var guid = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _repo.SaveAsync(new Journal { Guid = guid, AccountFk = "account1", Notes = "original", EnteredDate = now, UpdatedOn = now });
        await _repo.UpsertFromSyncAsync(new Journal { Guid = guid, AccountFk = "account1", Notes = "synced", EnteredDate = now, UpdatedOn = now + 1000 });

        var retrieved = await _repo.GetAsync(guid);
        Assert.NotNull(retrieved);
        Assert.Equal("synced", retrieved!.Notes);
    }

    [Fact]
    public async Task GetAllActiveAsync_ExcludesOtherAccounts()
    {
        var account1 = System.Guid.NewGuid().ToString();
        var account2 = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Journal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account1, Notes = "mine", EnteredDate = now, UpdatedOn = now });
        await _db.InsertOrReplaceAsync(new Journal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account2, Notes = "theirs", EnteredDate = now, UpdatedOn = now });

        var results = await _repo.GetAllActiveAsync(account1);

        Assert.Single(results);
        Assert.Equal("mine", results[0].Notes);
    }

    [Fact]
    public async Task GetAllActiveAsync_OrdersByEnteredDateDescending()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var older = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds();
        var newer = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Journal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId,
            Notes = "older entry", EnteredDate = older, UpdatedOn = older
        });
        await _db.InsertOrReplaceAsync(new Journal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId,
            Notes = "newer entry", EnteredDate = newer, UpdatedOn = newer
        });

        var results = await _repo.GetAllActiveAsync(accountId);

        Assert.Equal(2, results.Count);
        Assert.Equal("newer entry", results[0].Notes);
        Assert.Equal("older entry", results[1].Notes);
    }

    [Fact]
    public async Task GetModifiedSinceAsync_ExcludesOtherAccounts()
    {
        var account1 = System.Guid.NewGuid().ToString();
        var account2 = System.Guid.NewGuid().ToString();
        var tNew = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Journal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account1, Notes = "mine", EnteredDate = tNew, UpdatedOn = tNew });
        await _db.InsertOrReplaceAsync(new Journal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account2, Notes = "theirs", EnteredDate = tNew, UpdatedOn = tNew });

        var results = await _repo.GetModifiedSinceAsync(account1, 0);

        Assert.Single(results);
        Assert.Equal("mine", results[0].Notes);
    }

    [Fact]
    public async Task GetModifiedSinceAsync_IncludesSoftDeletedRecords()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Journal
        {
            Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId,
            Notes = "deleted", EnteredDate = now, UpdatedOn = now, DeletedAt = now
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

        await _repo.UpsertFromSyncAsync(new Journal
        {
            Guid = guid, AccountFk = "account1", Notes = "server note",
            EnteredDate = serverTs, UpdatedOn = serverTs
        });

        var retrieved = await _repo.GetAsync(guid);
        Assert.NotNull(retrieved);
        Assert.Equal(serverTs, retrieved!.UpdatedOn);
    }

    [Fact]
    public async Task GetAllActiveAsync_MultipleJournals_OrderedByEnteredDateDescending()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var older = DateTimeOffset.UtcNow.AddDays(-5).ToUnixTimeMilliseconds();
        var middle = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds();
        var newer = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Journal { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, Notes = "middle", EnteredDate = middle, UpdatedOn = middle });
        await _db.InsertOrReplaceAsync(new Journal { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, Notes = "older", EnteredDate = older, UpdatedOn = older });
        await _db.InsertOrReplaceAsync(new Journal { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, Notes = "newer", EnteredDate = newer, UpdatedOn = newer });

        var results = await _repo.GetAllActiveAsync(accountId);

        Assert.Equal(3, results.Count);
        Assert.Equal("newer", results[0].Notes);
        Assert.Equal("middle", results[1].Notes);
        Assert.Equal("older", results[2].Notes);
    }

    [Fact]
    public async Task GetAsync_WhenGuidNotFound_ReturnsNull()
    {
        var result = await _repo.GetAsync(System.Guid.NewGuid().ToString());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllActiveAsync_UpsertedSoftDeletedRecord_IsExcluded()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // A soft-deleted journal arriving via sync should not appear in GetAllActiveAsync
        await _repo.UpsertFromSyncAsync(new Journal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = accountId,
            Notes = "synced deleted note",
            EnteredDate = now,
            UpdatedOn = now,
            DeletedAt = now
        });

        var all = await _repo.GetAllActiveAsync(accountId);
        Assert.Empty(all);
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
    public async Task UpsertFromSyncAsync_PersistsAllOptionalFields()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = System.Guid.NewGuid().ToString();

        await _repo.UpsertFromSyncAsync(new Journal
        {
            Guid = guid, AccountFk = "account1", Notes = "synced note",
            Activity = "Running", Mood = "Energized", Tags = "fitness,outdoors",
            EnteredDate = now, UpdatedOn = now
        });

        var retrieved = await _repo.GetAsync(guid);
        Assert.NotNull(retrieved);
        Assert.Equal("Running", retrieved!.Activity);
        Assert.Equal("Energized", retrieved.Mood);
        Assert.Equal("fitness,outdoors", retrieved.Tags);
    }

    [Fact]
    public async Task DeleteAsync_JournalAppearsInGetModifiedSince()
    {
        var guid = System.Guid.NewGuid().ToString();
        var oldTs = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        await _db.InsertOrReplaceAsync(new Journal
        {
            Guid = guid, AccountFk = "account1", Notes = "to delete",
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
        var journal = new Journal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            Notes = "Today was productive",
            Activity = "Coding",
            Mood = "Focused",
            Tags = "work,tech",
            EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _repo.SaveAsync(journal);
        var retrieved = await _repo.GetAsync(journal.Guid);

        Assert.NotNull(retrieved);
        Assert.Equal("Coding", retrieved!.Activity);
        Assert.Equal("Focused", retrieved.Mood);
        Assert.Equal("work,tech", retrieved.Tags);
    }

    [Fact]
    public async Task GetAsync_WhenDeleted_StillReturnsRecord()
    {
        var guid = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _repo.SaveAsync(new Journal
        {
            Guid = guid, AccountFk = "account1", Notes = "to be deleted",
            EnteredDate = now, UpdatedOn = now
        });
        await _repo.DeleteAsync(guid);

        // GetAsync must return the record regardless of DeletedAt status
        var retrieved = await _repo.GetAsync(guid);
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved!.DeletedAt);
    }
}
