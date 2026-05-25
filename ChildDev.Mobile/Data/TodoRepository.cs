using LevelUp.Models;
using SQLite;

namespace LevelUp.Data;

public class TodoRepository(SQLiteAsyncConnection db)
{
    public Task SaveAsync(Todo todo)
    {
        todo.UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return db.InsertOrReplaceAsync(todo);
    }

    public Task CompleteAsync(string guid)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return db.ExecuteAsync("UPDATE Todo SET CompletedAt = ?, UpdatedOn = ? WHERE Guid = ?", now, now, guid);
    }

    public Task UncompleteAsync(string guid)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return db.ExecuteAsync("UPDATE Todo SET CompletedAt = NULL, UpdatedOn = ? WHERE Guid = ? AND CompletedAt IS NOT NULL", now, guid);
    }

    public Task DeleteAsync(string guid)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return db.ExecuteAsync("UPDATE Todo SET DeletedAt = ?, UpdatedOn = ? WHERE Guid = ?", now, now, guid);
    }

    public async Task<Todo?> GetAsync(string guid) =>
        await db.FindAsync<Todo>(guid);

    public Task<List<Todo>> GetPendingAsync(string accountFk) =>
        db.QueryAsync<Todo>(
            "SELECT * FROM Todo WHERE AccountFk = ? AND DeletedAt IS NULL AND CompletedAt IS NULL " +
            "ORDER BY (DueDate IS NULL), DueDate",
            accountFk);

    public Task<int> GetPendingCountAsync(string accountFk) =>
        db.Table<Todo>()
          .Where(t => t.AccountFk == accountFk && t.DeletedAt == null && t.CompletedAt == null)
          .CountAsync();

    public Task<int> GetOverdueCountAsync(string accountFk, long beforeMs) =>
        db.Table<Todo>()
          .Where(t => t.AccountFk == accountFk && t.DeletedAt == null && t.CompletedAt == null
                   && t.DueDate != null && t.DueDate < beforeMs)
          .CountAsync();

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

    public async Task SnoozeOverdueToTomorrowAsync(string accountFk, long todayStartMs)
    {
        var tomorrowMs = todayStartMs + 86_400_000L;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await db.ExecuteAsync(
            "UPDATE Todo SET DueDate = ?, UpdatedOn = ? WHERE AccountFk = ? AND DeletedAt IS NULL AND CompletedAt IS NULL AND DueDate IS NOT NULL AND DueDate < ?",
            tomorrowMs, ts, accountFk, todayStartMs);
    }

    public Task UpsertFromSyncAsync(Todo todo) =>
        db.InsertOrReplaceAsync(todo);
}
