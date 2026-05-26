using ChildDev.Api.Data;
using ChildDev.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Services;

public class WebReminderService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<Reminder>> GetPendingAsync(string accountGuid)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Reminders
            .Where(r => r.AccountGuid == accountGuid && !r.IsDismissed)
            .OrderBy(r => r.FireAt)
            .ToListAsync();
    }

    public async Task<List<Reminder>> GetDueAsync(string accountGuid)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        return await db.Reminders
            .Where(r => r.AccountGuid == accountGuid && !r.IsDismissed && r.FireAt <= now)
            .OrderBy(r => r.FireAt)
            .ToListAsync();
    }

    public async Task<Reminder> CreateAsync(string accountGuid, string title, string topic,
        string? entityGuid, string? entityLabel, DateTime fireAt)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var reminder = new Reminder
        {
            AccountGuid = accountGuid,
            Title = title,
            Topic = topic,
            EntityGuid = entityGuid,
            EntityLabel = entityLabel,
            FireAt = fireAt
        };
        db.Reminders.Add(reminder);
        await db.SaveChangesAsync();
        return reminder;
    }

    public async Task SnoozeAsync(int id, TimeSpan duration)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var reminder = await db.Reminders.FindAsync(id);
        if (reminder is null) return;
        reminder.FireAt = DateTime.UtcNow.Add(duration);
        await db.SaveChangesAsync();
    }

    public async Task DismissAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var reminder = await db.Reminders.FindAsync(id);
        if (reminder is null) return;
        reminder.IsDismissed = true;
        await db.SaveChangesAsync();
    }
}
