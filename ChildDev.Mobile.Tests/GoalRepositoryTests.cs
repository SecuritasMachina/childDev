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
}
