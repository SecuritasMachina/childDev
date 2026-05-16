using ChildDev.Mobile.Models;
using SQLite;

namespace ChildDev.Mobile.Data;

public class GoalProgressRepository(SQLiteAsyncConnection db)
{
    public Task SaveAsync(GoalProgress progress)
    {
        progress.UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return db.InsertOrReplaceAsync(progress);
    }

    public Task<List<GoalProgress>> GetForGoalAsync(string goalFk) =>
        db.Table<GoalProgress>()
          .Where(p => p.GoalFk == goalFk && p.DeletedAt == null)
          .OrderByDescending(p => p.UpdatedOn)
          .ToListAsync();

    public Task<List<GoalProgress>> GetModifiedSinceAsync(string accountFk, long since) =>
        db.Table<GoalProgress>()
          .Where(p => p.AccountFk == accountFk && p.UpdatedOn > since)
          .ToListAsync();

    public Task UpsertFromSyncAsync(GoalProgress progress) =>
        db.InsertOrReplaceAsync(progress);
}
