using System.Security.Claims;
using ChildDev.Api.Data;
using ChildDev.Api.Models.Dtos;
using ChildDev.Api.Models.Entities;
using ChildDev.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Endpoints;

public static class JournalEndpoints
{
    public static void MapJournalEndpoints(this WebApplication app)
    {
        app.MapPost("/api/sync/journal", async (
            SyncRequest<JournalDto> req,
            ClaimsPrincipal user,
            AppDbContext db,
            JwtService jwt) =>
        {
            var accountGuid = jwt.ExtractAccountGuid(user);
            if (accountGuid is null) return Results.Unauthorized();

            foreach (var dto in req.Records)
            {
                if (dto.AccountFk != accountGuid) continue;
                var existing = await db.Journals.FindAsync(dto.Guid);
                if (existing is null)
                    db.Journals.Add(DtoToEntity(dto));
                else if (dto.UpdatedOn > existing.UpdatedOn)
                    ApplyDto(existing, dto);
            }
            await db.SaveChangesAsync();

            var delta = await db.Journals
                .Where(j => j.AccountFk == accountGuid && j.UpdatedOn > req.LastSyncAt)
                .Select(j => EntityToDto(j))
                .ToListAsync();

            return Results.Ok(new SyncResponse<JournalDto>(delta));
        }).RequireAuthorization();
    }

    private static Journal DtoToEntity(JournalDto dto) => new()
    {
        Guid = dto.Guid, AccountFk = dto.AccountFk, Notes = dto.Notes,
        Activity = dto.Activity, Mood = dto.Mood, Tags = dto.Tags,
        EnteredDate = dto.EnteredDate, UpdatedOn = dto.UpdatedOn, DeletedAt = dto.DeletedAt
    };

    private static void ApplyDto(Journal entity, JournalDto dto)
    {
        entity.Notes = dto.Notes; entity.Activity = dto.Activity;
        entity.Mood = dto.Mood; entity.Tags = dto.Tags;
        entity.EnteredDate = dto.EnteredDate; entity.UpdatedOn = dto.UpdatedOn;
        entity.DeletedAt = dto.DeletedAt;
    }

    private static JournalDto EntityToDto(Journal j) => new(
        j.Guid, j.AccountFk, j.Notes, j.Activity, j.Mood, j.Tags,
        j.EnteredDate, j.UpdatedOn, j.DeletedAt);
}
