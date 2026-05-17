using ChildDev.Mobile.Models;
using SQLite;

namespace ChildDev.Mobile.Data;

public class TodoRepository(SQLiteAsyncConnection db)
{
    public Task SaveAsync(Todo todo)
    {
        todo.UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return db.InsertOrReplaceAsync(todo);
    }

    public async Task CompleteAsync(string guid)
    {
        var item = await db.FindAsync<Todo>(guid);
        if (item is null) return;
        item.CompletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        item.UpdatedOn = item.CompletedAt.Value;
        await db.UpdateAsync(item);
    }

    public async Task UncompleteAsync(string guid)
    {
        var item = await db.FindAsync<Todo>(guid);
        if (item is null || item.CompletedAt is null) return;
        item.CompletedAt = null;
        item.UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await db.UpdateAsync(item);
    }

    public async Task DeleteAsync(string guid)
    {
        var item = await db.FindAsync<Todo>(guid);
        if (item is null) return;
        item.DeletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        item.UpdatedOn = item.DeletedAt.Value;
        await db.UpdateAsync(item);
    }

    public async Task<Todo?> GetAsync(string guid) =>
        await db.FindAsync<Todo>(guid);

    public Task<List<Todo>> GetPendingAsync(string accountFk) =>
        db.QueryAsync<Todo>(
            "SELECT * FROM Todo WHERE AccountFk = ? AND DeletedAt IS NULL AND CompletedAt IS NULL " +
            "ORDER BY (DueDate IS NULL), DueDate",
            accountFk);

    public Task<List<Todo>> GetAllActiveAsync(string accountFk) =>
        db.Table<Todo>()
          .Where(t => t.AccountFk == accountFk && t.DeletedAt == null)
          .OrderByDescending(t => t.UpdatedOn)
          .ToListAsync();

    public Task<int> GetCompletedCountAsync(string accountFk) =>
        db.Table<Todo>()
          .Where(t => t.AccountFk == accountFk && t.DeletedAt == null && t.CompletedAt != null)
          .CountAsync();

    public Task<List<Todo>> GetCompletedAsync(string accountFk) =>
        db.Table<Todo>()
          .Where(t => t.AccountFk == accountFk && t.DeletedAt == null && t.CompletedAt != null)
          .OrderByDescending(t => t.CompletedAt)
          .ToListAsync();

    public Task<List<Todo>> GetModifiedSinceAsync(string accountFk, long since) =>
        db.Table<Todo>()
          .Where(t => t.AccountFk == accountFk && t.UpdatedOn > since)
          .ToListAsync();

    public Task UpsertFromSyncAsync(Todo todo) =>
        db.InsertOrReplaceAsync(todo);
}
