using System.Security.Claims;
using ChildDev.Api.Services;

namespace ChildDev.Api.Endpoints;

public static class MobileAnalyticsEndpoints
{
    public record MobileEventDto(string EventName, string? Context);

    public static void MapMobileAnalyticsEndpoints(this WebApplication app)
    {
        app.MapPost("/api/mobile/events", async (
            MobileEventDto[] events, ClaimsPrincipal user, JwtService jwt, WebAnalyticsService analytics) =>
        {
            var accountGuid = jwt.ExtractAccountGuid(user);
            if (accountGuid is null) return Results.Unauthorized();
            if (events is null || events.Length == 0) return Results.NoContent();
            if (events.Length > 100) return Results.Problem("Too many events.", statusCode: 400);

            foreach (var e in events)
            {
                if (string.IsNullOrWhiteSpace(e.EventName)) continue;
                await analytics.TrackAsync(e.EventName, accountGuid, "mobile", e.Context);
            }
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
