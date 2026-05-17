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
        SqliteFixture.EnsureInit();
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

    [Fact]
    public async Task CompleteAsync_SetsUpdatedOnToCompletedAt()
    {
        var guid = System.Guid.NewGuid().ToString();
        var todo = new Todo
        {
            Guid = guid,
            AccountFk = "account1",
            Title = "Sync after complete",
            UpdatedOn = 1000L
        };
        await _db.InsertOrReplaceAsync(todo);

        await _repo.CompleteAsync(guid);

        var retrieved = await _repo.GetAsync(guid);
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved!.CompletedAt);
        Assert.Equal(retrieved.CompletedAt!.Value, retrieved.UpdatedOn);
        Assert.True(retrieved.UpdatedOn > 1000L);
    }

    [Fact]
    public async Task Delete_SoftDeletes_ExcludedFromPending()
    {
        var todo = new Todo
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            Title = "To delete",
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _repo.SaveAsync(todo);
        await _repo.DeleteAsync(todo.Guid);

        var pending = await _repo.GetPendingAsync("account1");
        Assert.Empty(pending);
        var retrieved = await _repo.GetAsync(todo.Guid);
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.DeletedAt);
        Assert.Equal(retrieved.DeletedAt!.Value, retrieved.UpdatedOn);
    }

    [Fact]
    public async Task GetModifiedSince_ReturnsOnlyNewerRecords()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var tOld = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        var tNew = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Todo { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, Title = "old", UpdatedOn = tOld });
        await _db.InsertOrReplaceAsync(new Todo { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, Title = "new", UpdatedOn = tNew });

        var modified = await _repo.GetModifiedSinceAsync(accountId, tOld);
        Assert.Single(modified);
        Assert.Equal("new", modified[0].Title);
    }

    [Fact]
    public async Task GetCompletedCount_CountsCompletedExcludesDeleted()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Todo { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, Title = "pending", UpdatedOn = now });
        await _db.InsertOrReplaceAsync(new Todo { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, Title = "completed", UpdatedOn = now, CompletedAt = now });
        await _db.InsertOrReplaceAsync(new Todo { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, Title = "completed+deleted", UpdatedOn = now, CompletedAt = now, DeletedAt = now });

        var count = await _repo.GetCompletedCountAsync(accountId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetPendingAsync_DueDateTodosOrderedBeforeNullDueDate()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var tomorrow = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Todo { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, Title = "no due date", UpdatedOn = now });
        await _db.InsertOrReplaceAsync(new Todo { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, Title = "has due date", UpdatedOn = now, DueDate = tomorrow });

        var pending = await _repo.GetPendingAsync(accountId);
        Assert.Equal(2, pending.Count);
        Assert.Equal("has due date", pending[0].Title);
        Assert.Equal("no due date", pending[1].Title);
    }

    [Fact]
    public async Task UpsertFromSync_OverwritesExistingRecord()
    {
        var guid = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _repo.SaveAsync(new Todo { Guid = guid, AccountFk = "account1", Title = "original", UpdatedOn = now });
        await _repo.UpsertFromSyncAsync(new Todo { Guid = guid, AccountFk = "account1", Title = "synced", UpdatedOn = now + 1000 });

        var retrieved = await _repo.GetAsync(guid);
        Assert.NotNull(retrieved);
        Assert.Equal("synced", retrieved.Title);
    }

    [Fact]
    public async Task GetPendingAsync_ExcludesOtherAccounts()
    {
        var account1 = System.Guid.NewGuid().ToString();
        var account2 = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Todo { Guid = System.Guid.NewGuid().ToString(), AccountFk = account1, Title = "my task", UpdatedOn = now });
        await _db.InsertOrReplaceAsync(new Todo { Guid = System.Guid.NewGuid().ToString(), AccountFk = account2, Title = "their task", UpdatedOn = now });

        var results = await _repo.GetPendingAsync(account1);

        Assert.Single(results);
        Assert.Equal("my task", results[0].Title);
    }

    [Fact]
    public async Task SaveAsync_Edit_BumpsUpdatedOn()
    {
        var guid = System.Guid.NewGuid().ToString();
        var todo = new Todo
        {
            Guid = guid, AccountFk = "account1", Title = "original",
            UpdatedOn = 1000L
        };
        await _db.InsertOrReplaceAsync(todo);

        todo.Title = "edited";
        await _repo.SaveAsync(todo);

        var saved = await _repo.GetAsync(guid);
        Assert.NotNull(saved);
        Assert.Equal("edited", saved!.Title);
        Assert.True(saved.UpdatedOn > 1000L);
    }

    [Fact]
    public async Task SaveAsync_Edit_AppearsInGetModifiedSince()
    {
        var guid = System.Guid.NewGuid().ToString();
        var oldTs = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        var todo = new Todo
        {
            Guid = guid, AccountFk = "account5", Title = "original",
            UpdatedOn = oldTs
        };
        await _db.InsertOrReplaceAsync(todo);

        todo.Title = "edited";
        await _repo.SaveAsync(todo);

        var modified = await _repo.GetModifiedSinceAsync("account5", oldTs);
        Assert.Single(modified);
        Assert.Equal("edited", modified[0].Title);
    }

    [Fact]
    public async Task GetAllActiveAsync_IncludesCompletedExcludesDeleted()
    {
        var accountId = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Todo { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, Title = "pending", UpdatedOn = now });
        await _db.InsertOrReplaceAsync(new Todo { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, Title = "completed", UpdatedOn = now, CompletedAt = now });
        await _db.InsertOrReplaceAsync(new Todo { Guid = System.Guid.NewGuid().ToString(), AccountFk = accountId, Title = "deleted", UpdatedOn = now, DeletedAt = now });

        var all = await _repo.GetAllActiveAsync(accountId);

        Assert.Equal(2, all.Count);
        Assert.Contains(all, t => t.Title == "pending");
        Assert.Contains(all, t => t.Title == "completed");
        Assert.DoesNotContain(all, t => t.Title == "deleted");
    }

    [Fact]
    public async Task GetAllActiveAsync_ExcludesOtherAccounts()
    {
        var account1 = System.Guid.NewGuid().ToString();
        var account2 = System.Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Todo { Guid = System.Guid.NewGuid().ToString(), AccountFk = account1, Title = "mine", UpdatedOn = now });
        await _db.InsertOrReplaceAsync(new Todo { Guid = System.Guid.NewGuid().ToString(), AccountFk = account2, Title = "theirs", UpdatedOn = now });

        var all = await _repo.GetAllActiveAsync(account1);

        Assert.Single(all);
        Assert.Equal("mine", all[0].Title);
    }

    [Fact]
    public async Task GetModifiedSinceAsync_ExcludesOtherAccounts()
    {
        var account1 = System.Guid.NewGuid().ToString();
        var account2 = System.Guid.NewGuid().ToString();
        var tNew = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _db.InsertOrReplaceAsync(new Todo { Guid = System.Guid.NewGuid().ToString(), AccountFk = account1, Title = "mine", UpdatedOn = tNew });
        await _db.InsertOrReplaceAsync(new Todo { Guid = System.Guid.NewGuid().ToString(), AccountFk = account2, Title = "theirs", UpdatedOn = tNew });

        var results = await _repo.GetModifiedSinceAsync(account1, 0);

        Assert.Single(results);
        Assert.Equal("mine", results[0].Title);
    }
}
