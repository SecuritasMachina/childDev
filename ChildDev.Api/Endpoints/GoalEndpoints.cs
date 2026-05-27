using System.Security.Claims;
using ChildDev.Api.Data;
using ChildDev.Api.Models.Dtos;
using ChildDev.Api.Models.Entities;
using ChildDev.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Endpoints;

public static class GoalEndpoints
{
    public static void MapGoalEndpoints(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("sync.goal");
        app.MapPost("/api/sync/goal", async (
            SyncRequest<GoalDto> req, ClaimsPrincipal user, AppDbContext db, JwtService jwt) =>
        {
            var accountGuid = jwt.ExtractAccountGuid(user);
            if (accountGuid is null) return Results.Unauthorized();
            if (req.Records is null) return Results.Problem("Records must not be null.", statusCode: 400);
            if (req.Records.Count > 500) return Results.Problem("Records must not exceed 500 per sync.", statusCode: 400);
            var maxFutureMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 300_000;
            if (req.Records.Any(r => r.UpdatedOn > maxFutureMs))
            {
                logger.LogWarning("sync/goal account={Account} rejected: future UpdatedOn", accountGuid[..8]);
                return Results.Problem("Record UpdatedOn is too far in the future.", statusCode: 422);
            }
            if (req.Records.Any(r => !Guid.TryParse(r.Guid, out _)))
            {
                logger.LogWarning("sync/goal account={Account} rejected: invalid Guid", accountGuid[..8]);
                return Results.Problem("Record Guid is not a valid GUID.", statusCode: 422);
            }
            if (req.Records.Select(r => r.Guid).Distinct().Count() != req.Records.Count)
            {
                logger.LogWarning("sync/goal account={Account} rejected: duplicate Guid", accountGuid[..8]);
                return Results.Problem("Records must not contain duplicate Guids.", statusCode: 422);
            }
            var maxFutureTimestampMs = DateTimeOffset.UtcNow.AddYears(10).ToUnixTimeMilliseconds();
            if (req.Records.Any(r => r.EnteredDate > maxFutureTimestampMs))
            {
                logger.LogWarning("sync/goal account={Account} rejected: future EnteredDate", accountGuid[..8]);
                return Results.Problem("Record EnteredDate is too far in the future.", statusCode: 422);
            }
            if (req.Records.Any(r => r.CompletionDate.HasValue && r.CompletionDate.Value > maxFutureTimestampMs))
            {
                logger.LogWarning("sync/goal account={Account} rejected: future CompletionDate", accountGuid[..8]);
                return Results.Problem("Record CompletionDate is too far in the future.", statusCode: 422);
            }
            if (req.Records.Any(r => r.ExpirationDate.HasValue && r.ExpirationDate.Value > maxFutureTimestampMs))
            {
                logger.LogWarning("sync/goal account={Account} rejected: future ExpirationDate", accountGuid[..8]);
                return Results.Problem("Record ExpirationDate is too far in the future.", statusCode: 422);
            }
            if (req.Records.Any(r => r.NextMeetingDate.HasValue && r.NextMeetingDate.Value > maxFutureTimestampMs))
            {
                logger.LogWarning("sync/goal account={Account} rejected: NextMeetingDate too far in future", accountGuid[..8]);
                return Results.Problem("Record NextMeetingDate is too far in the future.", statusCode: 422);
            }
            if (req.Records.Any(r => r.DeletedAt is null && r.CompletionDate is null && string.IsNullOrWhiteSpace(r.GoalText)))
            {
                logger.LogWarning("sync/goal account={Account} rejected: blank GoalText", accountGuid[..8]);
                return Results.Problem("Record GoalText must not be blank.", statusCode: 422);
            }
            if (req.Records.Any(r => r.GoalText?.Length > 2_000))
            {
                logger.LogWarning("sync/goal account={Account} rejected: GoalText too long", accountGuid[..8]);
                return Results.Problem("Record GoalText must not exceed 2000 characters.", statusCode: 422);
            }
            if (req.Records.Any(r => r.MeasurableOutcome?.Length > 2_000))
            {
                logger.LogWarning("sync/goal account={Account} rejected: MeasurableOutcome too long", accountGuid[..8]);
                return Results.Problem("Record MeasurableOutcome must not exceed 2000 characters.", statusCode: 422);
            }
            if (req.Records.Any(r => r.Steps?.Length > 2_000))
            {
                logger.LogWarning("sync/goal account={Account} rejected: Steps too long", accountGuid[..8]);
                return Results.Problem("Record Steps must not exceed 2000 characters.", statusCode: 422);
            }
            if (req.Records.Any(r => r.DeletedAt.HasValue && r.DeletedAt.Value > r.UpdatedOn))
            {
                logger.LogWarning("sync/goal account={Account} rejected: DeletedAt > UpdatedOn", accountGuid[..8]);
                return Results.Problem("Record DeletedAt must not exceed UpdatedOn.", statusCode: 422);
            }
            var mismatchCount = req.Records.Count(r => r.AccountFk != accountGuid);
            if (mismatchCount > 0)
                logger.LogWarning("sync/goal account={Account} skipped {Skipped} records with mismatched AccountFk",
                    accountGuid[..8], mismatchCount);

            var incomingGuids = req.Records.Select(r => r.Guid).ToList();
            var existingMap = await db.Goals
                .Where(g => incomingGuids.Contains(g.Guid))
                .ToDictionaryAsync(g => g.Guid);
            foreach (var dto in req.Records)
            {
                if (dto.AccountFk != accountGuid) continue;
                if (!existingMap.TryGetValue(dto.Guid, out var entity))
                    db.Goals.Add(DtoToEntity(dto));
                else if (dto.UpdatedOn > entity.UpdatedOn) ApplyDto(entity, dto);
            }
            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
            var delta = await db.Goals
                .Where(g => g.AccountFk == accountGuid && g.UpdatedOn > req.LastSyncAt)
                .OrderBy(g => g.UpdatedOn)
                .Select(g => EntityToDto(g)).ToListAsync();
            logger.LogDebug("sync/goal account={Account} incoming={Incoming} delta={Delta}",
                accountGuid[..8], req.Records.Count, delta.Count);
            return Results.Ok(new SyncResponse<GoalDto>(delta));
        }).RequireAuthorization();
    }

    private static Goal DtoToEntity(GoalDto dto) => new()
    {
        Guid = dto.Guid, AccountFk = dto.AccountFk, GoalText = dto.GoalText,
        NextMeetingDate = dto.NextMeetingDate, ExpirationDate = dto.ExpirationDate,
        EnteredDate = dto.EnteredDate, MeasurableOutcome = dto.MeasurableOutcome,
        CompletionDate = dto.CompletionDate, UpdatedOn = dto.UpdatedOn, DeletedAt = dto.DeletedAt,
        ProgressPercent = dto.ProgressPercent, Category = dto.Category, IsPinned = dto.IsPinned,
        Steps = dto.Steps
    };
    private static void ApplyDto(Goal e, GoalDto dto)
    {
        e.GoalText = dto.GoalText; e.NextMeetingDate = dto.NextMeetingDate;
        e.ExpirationDate = dto.ExpirationDate; e.MeasurableOutcome = dto.MeasurableOutcome;
        e.CompletionDate = dto.CompletionDate; e.UpdatedOn = dto.UpdatedOn; e.DeletedAt = dto.DeletedAt;
        e.ProgressPercent = dto.ProgressPercent; e.Category = dto.Category; e.IsPinned = dto.IsPinned;
        e.Steps = dto.Steps;
    }
    private static GoalDto EntityToDto(Goal g) => new(
        g.Guid, g.AccountFk, g.GoalText, g.NextMeetingDate, g.ExpirationDate,
        g.EnteredDate, g.MeasurableOutcome, g.CompletionDate, g.UpdatedOn, g.DeletedAt,
        g.ProgressPercent, g.Category, g.IsPinned, g.Steps);
}
