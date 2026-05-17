using BCrypt.Net;
using ChildDev.Api.Data;
using ChildDev.Api.Models.Dtos;
using ChildDev.Api.Models.Entities;
using ChildDev.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/register", async (RegisterRequest req, AppDbContext db, JwtService jwt) =>
        {
            var nickName = req.NickName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nickName))
                return Results.Problem("NickName must not be empty.", statusCode: 400);
            if (nickName.Length > 50)
                return Results.Problem("NickName must not exceed 50 characters.", statusCode: 400);
            if (string.IsNullOrWhiteSpace(req.PinHash))
                return Results.Problem("PinHash must not be empty.", statusCode: 400);
            if (req.PinHash.Length > 200)
                return Results.Problem("PinHash must not exceed 200 characters.", statusCode: 400);
            if (await db.Accounts.AnyAsync(a => a.NickName == nickName))
                return Results.Conflict("Nickname already taken");

            var account = new Account
            {
                Guid = Guid.NewGuid().ToString(),
                NickName = nickName,
                PinHash = BCrypt.Net.BCrypt.HashPassword(req.PinHash),
                CreatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            db.Accounts.Add(account);
            await db.SaveChangesAsync();

            return Results.Created($"/api/auth/{account.Guid}",
                new AuthResponse(jwt.Issue(account.Guid), account.Guid));
        });

        app.MapPost("/api/auth/token", async (TokenRequest req, AppDbContext db, JwtService jwt) =>
        {
            var nickName = req.NickName?.Trim();
            var account = await db.Accounts.FirstOrDefaultAsync(a => a.NickName == nickName);
            if (account is null || !BCrypt.Net.BCrypt.Verify(req.PinHash, account.PinHash))
                return Results.Unauthorized();

            return Results.Ok(new AuthResponse(jwt.Issue(account.Guid), account.Guid));
        });
    }
}
