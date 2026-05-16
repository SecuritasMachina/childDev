using ChildDev.Mobile.Models;
using SQLite;

namespace ChildDev.Mobile.Data;

public class JournalRepository(SQLiteAsyncConnection db)
{
    public Task SaveAsync(Journal journal)
    {
        if (journal.UpdatedOn == 0)
        {
            journal.UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        return db.InsertOrReplaceAsync(journal);
    }

    public async Task DeleteAsync(string guid)
    {
        var item = await db.FindAsync<Journal>(guid);
        if (item is null) return;
        item.DeletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        item.UpdatedOn = item.DeletedAt.Value;
        await db.UpdateAsync(item);
    }

    public Task<List<Journal>> GetAllActiveAsync(string accountFk) =>
        db.Table<Journal>()
          .Where(j => j.AccountFk == accountFk && j.DeletedAt == null)
          .OrderByDescending(j => j.EnteredDate)
          .ToListAsync();

    public async Task<Journal?> GetAsync(string guid) =>
        await db.FindAsync<Journal>(guid);

    public Task<List<Journal>> GetModifiedSinceAsync(string accountFk, long since) =>
        db.Table<Journal>()
          .Where(j => j.AccountFk == accountFk && j.UpdatedOn > since)
          .ToListAsync();

    public Task UpsertFromSyncAsync(Journal journal) =>
        db.InsertOrReplaceAsync(journal);
}
