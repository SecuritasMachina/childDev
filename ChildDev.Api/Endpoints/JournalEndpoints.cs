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
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("sync.journal");
        app.MapPost("/api/sync/journal", async (
            SyncRequest<JournalDto> req,
            ClaimsPrincipal user,
            AppDbContext db,
            JwtService jwt) =>
        {
            var accountGuid = jwt.ExtractAccountGuid(user);
            if (accountGuid is null) return Results.Unauthorized();
            if (req.Records is null) return Results.BadRequest("Records must not be null.");
            if (req.Records.Count > 500) return Results.BadRequest("Records must not exceed 500 per sync.");
            var maxFutureMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 300_000;
            if (req.Records.Any(r => r.UpdatedOn > maxFutureMs))
                return Results.UnprocessableEntity("Record UpdatedOn is too far in the future.");

            var incomingGuids = req.Records.Select(r => r.Guid).ToList();
            var existingMap = await db.Journals
                .Where(j => incomingGuids.Contains(j.Guid))
                .ToDictionaryAsync(j => j.Guid);
            foreach (var dto in req.Records)
            {
                if (dto.AccountFk != accountGuid) continue;
                if (!existingMap.TryGetValue(dto.Guid, out var entity))
                    db.Journals.Add(DtoToEntity(dto));
                else if (dto.UpdatedOn > entity.UpdatedOn)
                    ApplyDto(entity, dto);
            }
            await db.SaveChangesAsync();

            var delta = await db.Journals
                .Where(j => j.AccountFk == accountGuid && j.UpdatedOn > req.LastSyncAt)
                .OrderBy(j => j.UpdatedOn)
                .Select(j => EntityToDto(j))
                .ToListAsync();

            logger.LogDebug("sync/journal account={Account} incoming={Incoming} delta={Delta}",
                accountGuid[..8], req.Records.Count, delta.Count);
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
