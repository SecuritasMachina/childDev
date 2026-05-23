using LevelUp.Models;
using SQLite;

namespace LevelUp.Data;

public class GoalRepository(SQLiteAsyncConnection db)
{
    public Task SaveAsync(Goal goal)
    {
        goal.UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return db.InsertOrReplaceAsync(goal);
    }

    public Task DeleteAsync(string guid)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return db.ExecuteAsync("UPDATE Goal SET DeletedAt = ?, UpdatedOn = ? WHERE Guid = ?", now, now, guid);
    }

    public Task CompleteAsync(string guid)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return db.ExecuteAsync("UPDATE Goal SET CompletionDate = ?, UpdatedOn = ? WHERE Guid = ?", now, now, guid);
    }

    public Task ReopenAsync(string guid)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return db.ExecuteAsync("UPDATE Goal SET CompletionDate = NULL, UpdatedOn = ? WHERE Guid = ?", now, guid);
    }

    public Task<List<Goal>> GetAllActiveAsync(string accountFk) =>
        db.QueryAsync<Goal>(
            "SELECT * FROM Goal WHERE AccountFk = ? AND DeletedAt IS NULL " +
            "ORDER BY (CompletionDate IS NOT NULL), EnteredDate DESC",
            accountFk);

    public async Task<Goal?> GetAsync(string guid) =>
        await db.FindAsync<Goal>(guid);

    public Task<List<Goal>> GetModifiedSinceAsync(string accountFk, long since) =>
        db.Table<Goal>()
          .Where(g => g.AccountFk == accountFk && g.UpdatedOn > since)
          .ToListAsync();

    public async Task UpsertFromSyncAsync(Goal goal)
    {
        var existing = await db.FindAsync<Goal>(goal.Guid);
        if (existing is not null)
            goal.EnteredDate = existing.EnteredDate;
        await db.InsertOrReplaceAsync(goal);
    }
}
