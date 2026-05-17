using ChildDev.Mobile.Models;
using SQLite;

namespace ChildDev.Mobile.Data;

public class GoalRepository(SQLiteAsyncConnection db)
{
    public Task SaveAsync(Goal goal)
    {
        goal.UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return db.InsertOrReplaceAsync(goal);
    }

    public async Task DeleteAsync(string guid)
    {
        var item = await db.FindAsync<Goal>(guid);
        if (item is null) return;
        item.DeletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        item.UpdatedOn = item.DeletedAt.Value;
        await db.UpdateAsync(item);
    }

    public async Task CompleteAsync(string guid)
    {
        var item = await db.FindAsync<Goal>(guid);
        if (item is null) return;
        item.CompletionDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        item.UpdatedOn = item.CompletionDate.Value;
        await db.UpdateAsync(item);
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
