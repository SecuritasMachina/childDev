using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ChildDev.Api.Services;

public class JwtService(IConfiguration config)
{
    private readonly string _secret = config["CHILDDEV_JWT_SECRET"]
        ?? throw new InvalidOperationException("CHILDDEV_JWT_SECRET is not configured");

    public string Issue(string accountGuid)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: [new Claim("accountGuid", accountGuid)],
            expires: DateTime.UtcNow.AddDays(90),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string? ExtractAccountGuid(ClaimsPrincipal principal) =>
        principal.FindFirst("accountGuid")?.Value;
}
