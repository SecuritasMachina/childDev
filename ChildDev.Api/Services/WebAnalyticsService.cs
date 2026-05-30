using ChildDev.Api.Data;
using ChildDev.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Services;

public class WebAnalyticsService(IDbContextFactory<AppDbContext> dbFactory, BizEyesClient bizEyes)
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

        // Also forward to the external AnalyticsHub (bizeyes) dashboard. Fire-and-forget.
        // Pages emit a "page_view" event with the page name; map those to bizeyes page views.
        if (eventName == "page_view")
            bizEyes.TrackPageView(page is null ? "/" : "/" + page, accountGuid, page);
        else
            bizEyes.TrackEvent(eventName, accountGuid, page); // context withheld (may be free text)
    }
}
