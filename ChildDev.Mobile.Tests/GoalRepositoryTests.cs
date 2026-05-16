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
}
