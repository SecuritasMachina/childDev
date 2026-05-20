using ChildDev.Api.Services;

namespace ChildDev.Api.Endpoints;

public static class WebAuthEndpoints
{
    public static void MapWebAuthEndpoints(this WebApplication app)
    {
        // Consumes a short-lived token (set by interactive Blazor after register/login)
        // and writes the session, then redirects. This avoids session-write restrictions
        // in interactive Blazor Server components.
        app.MapGet("/api/web/auth/complete", async (string token, WebAuthTokenService tokens, HttpContext ctx) =>
        {
            var auth = tokens.ConsumeToken(token);
            if (auth is null) return Results.Redirect("/login");

            ctx.Session.SetString("AccountGuid", auth.Value.AccountGuid);
            ctx.Session.SetString("NickName", auth.Value.NickName);
            await ctx.Session.CommitAsync();

            return Results.Redirect("/");
        });

        app.MapGet("/api/web/auth/logout", async (HttpContext ctx, WebAnalyticsService analytics) =>
        {
            var accountGuid = ctx.Session.GetString("AccountGuid");
            await analytics.TrackAsync("logout", accountGuid, "logout");
            ctx.Session.Clear();
            await ctx.Session.CommitAsync();

            return Results.Redirect("/login");
        });
    }
}
