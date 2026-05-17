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
}
