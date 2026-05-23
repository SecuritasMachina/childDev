using ChildDev.Api.Data;
using ChildDev.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Services;

public class WebAnalyticsService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task TrackAsync(string eventName, string? accountGuid, string? page, string? context = null)
    {
        await using var db = dbFactory.CreateDbContext();
        db.AnalyticsEvents.Add(new AnalyticsEvent
        {
            EventName = eventName,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AccountGuid = accountGuid,
            Page = page,
            Context = context
        });
        await db.SaveChangesAsync();
    }
}
