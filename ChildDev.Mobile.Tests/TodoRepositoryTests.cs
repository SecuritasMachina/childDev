using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using SQLite;

namespace ChildDev.Mobile.Tests;

public class TodoRepositoryTests : IDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private readonly TodoRepository _repo;

    public TodoRepositoryTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _db = new SQLiteAsyncConnection(":memory:");
        _db.CreateTableAsync<Todo>().GetAwaiter().GetResult();
        _repo = new TodoRepository(_db);
    }

    public void Dispose() => _db.CloseAsync().GetAwaiter().GetResult();

    [Fact]
    public async Task Save_NewTodo_CanBeRetrieved()
    {
        var todo = new Todo
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            Title = "Buy milk",
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _repo.SaveAsync(todo);
        var pending = await _repo.GetPendingAsync("account1");
        Assert.Single(pending);
        Assert.Equal("Buy milk", pending[0].Title);
    }

    [Fact]
    public async Task Complete_SetCompletedAt_ExcludedFromPending()
    {
        var todo = new Todo
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            Title = "Done task",
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _repo.SaveAsync(todo);
        await _repo.CompleteAsync(todo.Guid);

        var pending = await _repo.GetPendingAsync("account1");
        Assert.Empty(pending);
    }
}
