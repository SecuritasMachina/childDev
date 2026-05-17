using ChildDev.Api.Data;
using ChildDev.Api.Models.Entities;

namespace ChildDev.Api.Services;

public class WebAnalyticsService(AppDbContext db)
{
    public async Task TrackAsync(string eventName, string? accountGuid, string? page, string? context = null)
    {
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
