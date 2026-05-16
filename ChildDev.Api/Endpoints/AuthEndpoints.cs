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
            if (await db.Accounts.AnyAsync(a => a.NickName == req.NickName))
                return Results.Conflict("Nickname already taken");

            var account = new Account
            {
                Guid = Guid.NewGuid().ToString(),
                NickName = req.NickName,
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
            var account = await db.Accounts.FirstOrDefaultAsync(a => a.NickName == req.NickName);
            if (account is null || !BCrypt.Net.BCrypt.Verify(req.PinHash, account.PinHash))
                return Results.Unauthorized();

            return Results.Ok(new AuthResponse(jwt.Issue(account.Guid), account.Guid));
        });
    }
}
