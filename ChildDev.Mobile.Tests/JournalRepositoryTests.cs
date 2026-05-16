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
    }

    [Fact]
    public async Task GetModifiedSince_ReturnsOnlyNewerRecords()
    {
        var t1 = 1000L;
        var t2 = 2000L;
        var accountId = "account2";

        await _repo.SaveAsync(new Journal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = accountId,
            Notes = "old",
            EnteredDate = t1,
            UpdatedOn = t1
        });
        await _repo.SaveAsync(new Journal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = accountId,
            Notes = "new",
            EnteredDate = t2,
            UpdatedOn = t2
        });

        var modified = await _repo.GetModifiedSinceAsync(accountId, t1);
        Assert.Single(modified);
        Assert.Equal("new", modified[0].Notes);
    }
}
