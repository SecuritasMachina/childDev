using LevelUp.Models;
using SQLite;

namespace LevelUp.Data;

public class ReminderRepository(SQLiteAsyncConnection db)
{
    public async Task<int> SaveAsync(Reminder reminder)
    {
        return await db.InsertOrReplaceAsync(reminder);
    }

    public Task<List<Reminder>> GetPendingAsync(string accountFk)
    {
        return db.Table<Reminder>()
            .Where(r => r.AccountFk == accountFk && !r.IsDismissed)
            .OrderBy(r => r.FireAt)
            .ToListAsync();
    }

    public Task<List<Reminder>> GetForEntityAsync(string entityGuid) =>
        db.Table<Reminder>()
            .Where(r => r.EntityGuid == entityGuid && !r.IsDismissed)
            .OrderBy(r => r.FireAt)
            .ToListAsync();

    public async Task<Reminder?> GetAsync(string guid) =>
        await db.FindAsync<Reminder>(guid);

    public Task DeleteAsync(string guid) =>
        db.DeleteAsync<Reminder>(guid);
}
