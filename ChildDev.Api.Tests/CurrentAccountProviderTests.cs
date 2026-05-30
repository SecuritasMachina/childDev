using ChildDev.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using Xunit;

namespace ChildDev.Api.Tests;

public class CurrentAccountProviderTests
{
    private static JwtService Jwt()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CHILDDEV_JWT_SECRET"] = "test-secret-test-secret-test-secret" })
            .Build();
        return new JwtService(cfg);
    }

    [Fact]
    public void Prefers_JwtClaim_OverSession()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("accountGuid", "JWT-ACC") }, "jwt"));
        var accessor = new HttpContextAccessor { HttpContext = ctx };
        var p = new CurrentAccountProvider(accessor, Jwt());
        Assert.Equal("JWT-ACC", p.GetAccountGuid());
    }

    [Fact]
    public void Returns_Null_WhenNoIdentity()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var p = new CurrentAccountProvider(accessor, Jwt());
        Assert.Null(p.GetAccountGuid());
    }
}
