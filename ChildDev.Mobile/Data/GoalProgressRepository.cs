using LevelUp.Models;
using SQLite;

namespace LevelUp.Data;

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

    public async Task<Dictionary<string, (string? Steps, long UpdatedOn, int Count)>> GetLatestProgressInfoAsync(string accountFk)
    {
        var rows = await db.QueryAsync<GoalProgress>(
            "SELECT GoalFk, NextStepItems, MAX(UpdatedOn) AS UpdatedOn, COUNT(*) AS ProgressCount FROM GoalProgress " +
            "WHERE AccountFk = ? AND DeletedAt IS NULL " +
            "GROUP BY GoalFk",
            accountFk);
        return rows.ToDictionary(p => p.GoalFk, p => (p.NextStepItems, p.UpdatedOn, p.ProgressCount));
    }

    public Task<List<GoalProgress>> GetModifiedSinceAsync(string accountFk, long since) =>
        db.Table<GoalProgress>()
          .Where(p => p.AccountFk == accountFk && p.UpdatedOn > since)
          .ToListAsync();

    public Task DeleteForGoalAsync(string goalFk)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return db.ExecuteAsync(
            "UPDATE GoalProgress SET DeletedAt = ?, UpdatedOn = ? WHERE GoalFk = ? AND DeletedAt IS NULL",
            now, now, goalFk);
    }

    public Task UpsertFromSyncAsync(GoalProgress progress) =>
        db.InsertOrReplaceAsync(progress);
}
