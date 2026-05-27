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

    private class ProgressSummary
    {
        public string GoalFk { get; set; } = string.Empty;
        public string? NextStepItems { get; set; }
        public long UpdatedOn { get; set; }
        public int ProgressCount { get; set; }
    }

    public async Task<Dictionary<string, (string? Steps, long UpdatedOn, int Count)>> GetLatestProgressInfoAsync(string accountFk)
    {
        var rows = await db.QueryAsync<ProgressSummary>(
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

    public async Task<int> GetCurrentStreakAsync(string accountFk)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-60).ToUnixTimeMilliseconds();
        var timestamps = await db.QueryAsync<GoalProgress>(
            "SELECT UpdatedOn FROM GoalProgress WHERE AccountFk = ? AND DeletedAt IS NULL AND UpdatedOn >= ? ORDER BY UpdatedOn DESC",
            accountFk, cutoff);
        var activeDates = timestamps
            .Select(p => DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(p.UpdatedOn).LocalDateTime))
            .ToHashSet();
        var day = DateOnly.FromDateTime(DateTime.Today);
        if (!activeDates.Contains(day)) day = day.AddDays(-1);
        var streak = 0;
        while (activeDates.Contains(day)) { streak++; day = day.AddDays(-1); }
        return streak;
    }

    public Task UpsertFromSyncAsync(GoalProgress progress) =>
        db.InsertOrReplaceAsync(progress);
}
