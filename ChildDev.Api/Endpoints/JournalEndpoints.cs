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
            if (req.Records is null) return Results.Problem("Records must not be null.", statusCode: 400);
            if (req.Records.Count > 500) return Results.Problem("Records must not exceed 500 per sync.", statusCode: 400);
            var maxFutureMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 300_000;
            if (req.Records.Any(r => r.UpdatedOn > maxFutureMs))
            {
                logger.LogWarning("sync/journal account={Account} rejected: future UpdatedOn", accountGuid[..8]);
                return Results.Problem("Record UpdatedOn is too far in the future.", statusCode: 422);
            }
            if (req.Records.Any(r => !Guid.TryParse(r.Guid, out _)))
            {
                logger.LogWarning("sync/journal account={Account} rejected: invalid Guid", accountGuid[..8]);
                return Results.Problem("Record Guid is not a valid GUID.", statusCode: 422);
            }
            if (req.Records.Select(r => r.Guid).Distinct().Count() != req.Records.Count)
            {
                logger.LogWarning("sync/journal account={Account} rejected: duplicate Guid", accountGuid[..8]);
                return Results.Problem("Records must not contain duplicate Guids.", statusCode: 422);
            }
            var maxEnteredDateMs = DateTimeOffset.UtcNow.AddYears(10).ToUnixTimeMilliseconds();
            if (req.Records.Any(r => r.EnteredDate > maxEnteredDateMs))
            {
                logger.LogWarning("sync/journal account={Account} rejected: future EnteredDate", accountGuid[..8]);
                return Results.Problem("Record EnteredDate is too far in the future.", statusCode: 422);
            }
            if (req.Records.Any(r => r.DeletedAt is null && string.IsNullOrWhiteSpace(r.Notes)))
            {
                logger.LogWarning("sync/journal account={Account} rejected: blank Notes", accountGuid[..8]);
                return Results.Problem("Record Notes must not be blank.", statusCode: 422);
            }
            if (req.Records.Any(r => r.Notes?.Length > 10_000))
            {
                logger.LogWarning("sync/journal account={Account} rejected: Notes too long", accountGuid[..8]);
                return Results.Problem("Record Notes must not exceed 10000 characters.", statusCode: 422);
            }
            if (req.Records.Any(r => r.Activity?.Length > 255))
            {
                logger.LogWarning("sync/journal account={Account} rejected: Activity too long", accountGuid[..8]);
                return Results.Problem("Record Activity must not exceed 255 characters.", statusCode: 422);
            }
            if (req.Records.Any(r => r.Mood?.Length > 50))
            {
                logger.LogWarning("sync/journal account={Account} rejected: Mood too long", accountGuid[..8]);
                return Results.Problem("Record Mood must not exceed 50 characters.", statusCode: 422);
            }
            if (req.Records.Any(r => r.Tags?.Length > 500))
            {
                logger.LogWarning("sync/journal account={Account} rejected: Tags too long", accountGuid[..8]);
                return Results.Problem("Record Tags must not exceed 500 characters.", statusCode: 422);
            }

            var mismatchCount = req.Records.Count(r => r.AccountFk != accountGuid);
            if (mismatchCount > 0)
                logger.LogWarning("sync/journal account={Account} skipped {Skipped} records with mismatched AccountFk",
                    accountGuid[..8], mismatchCount);

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
            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();

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
