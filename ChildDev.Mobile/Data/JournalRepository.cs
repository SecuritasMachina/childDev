using LevelUp.Models;
using SQLite;

namespace LevelUp.Data;

public class JournalRepository(SQLiteAsyncConnection db)
{
    public Task SaveAsync(Journal journal)
    {
        journal.UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return db.InsertOrReplaceAsync(journal);
    }

    public Task DeleteAsync(string guid)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return db.ExecuteAsync("UPDATE Journal SET DeletedAt = ?, UpdatedOn = ? WHERE Guid = ?", now, now, guid);
    }

    public Task<List<Journal>> GetAllActiveAsync(string accountFk) =>
        db.Table<Journal>()
          .Where(j => j.AccountFk == accountFk && j.DeletedAt == null)
          .OrderByDescending(j => j.EnteredDate)
          .ToListAsync();

    public Task<List<Journal>> GetRecentAsync(string accountFk, int count) =>
        db.Table<Journal>()
          .Where(j => j.AccountFk == accountFk && j.DeletedAt == null)
          .OrderByDescending(j => j.EnteredDate)
          .Take(count)
          .ToListAsync();

    public Task<int> GetCountSinceAsync(string accountFk, long sinceMs) =>
        db.Table<Journal>()
          .Where(j => j.AccountFk == accountFk && j.DeletedAt == null && j.EnteredDate >= sinceMs)
          .CountAsync();

    public async Task<Journal?> GetAsync(string guid) =>
        await db.FindAsync<Journal>(guid);

    public Task<List<Journal>> GetModifiedSinceAsync(string accountFk, long since) =>
        db.Table<Journal>()
          .Where(j => j.AccountFk == accountFk && j.UpdatedOn > since)
          .ToListAsync();

    public async Task UpsertFromSyncAsync(Journal journal)
    {
        var existing = await db.FindAsync<Journal>(journal.Guid);
        if (existing is not null)
            journal.EnteredDate = existing.EnteredDate;
        await db.InsertOrReplaceAsync(journal);
    }
}
