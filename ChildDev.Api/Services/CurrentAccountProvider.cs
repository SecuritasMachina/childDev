using Microsoft.AspNetCore.Http;

namespace ChildDev.Api.Services;

public sealed class CurrentAccountProvider(IHttpContextAccessor accessor, JwtService jwt) : ICurrentAccountProvider
{
    public string? GetAccountGuid()
    {
        var ctx = accessor.HttpContext;
        if (ctx is null) return null;
        var fromJwt = jwt.ExtractAccountGuid(ctx.User);
        if (!string.IsNullOrEmpty(fromJwt)) return fromJwt;
        try { return ctx.Session.GetString("AccountGuid"); }
        catch { return null; }
    }
}
