using System.Security.Claims;
using ChildDev.Api.Data;
using ChildDev.Api.Models.Dtos;
using ChildDev.Api.Models.Entities;
using ChildDev.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Endpoints;

public static class GoalProgressEndpoints
{
    public static void MapGoalProgressEndpoints(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("sync.goal-progress");
        app.MapPost("/api/sync/goal-progress", async (
            SyncRequest<GoalProgressDto> req, ClaimsPrincipal user, AppDbContext db, JwtService jwt) =>
        {
            var accountGuid = jwt.ExtractAccountGuid(user);
            if (accountGuid is null) return Results.Unauthorized();
            if (req.Records is null) return Results.Problem("Records must not be null.", statusCode: 400);
            if (req.Records.Count > 500) return Results.Problem("Records must not exceed 500 per sync.", statusCode: 400);
            var maxFutureMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 300_000;
            if (req.Records.Any(r => r.UpdatedOn > maxFutureMs))
            {
                logger.LogWarning("sync/goal-progress account={Account} rejected: future UpdatedOn", accountGuid[..8]);
                return Results.Problem("Record UpdatedOn is too far in the future.", statusCode: 422);
            }
            if (req.Records.Any(r => !Guid.TryParse(r.Guid, out _)))
            {
                logger.LogWarning("sync/goal-progress account={Account} rejected: invalid Guid", accountGuid[..8]);
                return Results.Problem("Record Guid is not a valid GUID.", statusCode: 422);
            }
            if (req.Records.Any(r => !Guid.TryParse(r.GoalFk, out _)))
            {
                logger.LogWarning("sync/goal-progress account={Account} rejected: invalid GoalFk", accountGuid[..8]);
                return Results.Problem("Record GoalFk is not a valid GUID.", statusCode: 422);
            }
            if (req.Records.Select(r => r.Guid).Distinct().Count() != req.Records.Count)
            {
                logger.LogWarning("sync/goal-progress account={Account} rejected: duplicate Guid", accountGuid[..8]);
                return Results.Problem("Records must not contain duplicate Guids.", statusCode: 422);
            }
            var maxFutureTimestampMs = DateTimeOffset.UtcNow.AddYears(10).ToUnixTimeMilliseconds();
            if (req.Records.Any(r => r.NextMeetingDate.HasValue && r.NextMeetingDate.Value > maxFutureTimestampMs))
            {
                logger.LogWarning("sync/goal-progress account={Account} rejected: NextMeetingDate too far in future", accountGuid[..8]);
                return Results.Problem("Record NextMeetingDate is too far in the future.", statusCode: 422);
            }
            if (req.Records.Any(r => r.DeletedAt is null && string.IsNullOrWhiteSpace(r.NextStepItems)))
            {
                logger.LogWarning("sync/goal-progress account={Account} rejected: blank NextStepItems", accountGuid[..8]);
                return Results.Problem("Record NextStepItems must not be blank.", statusCode: 422);
            }
            if (req.Records.Any(r => r.NextStepItems?.Length > 2_000))
            {
                logger.LogWarning("sync/goal-progress account={Account} rejected: NextStepItems too long", accountGuid[..8]);
                return Results.Problem("Record NextStepItems must not exceed 2000 characters.", statusCode: 422);
            }
            var mismatchCount = req.Records.Count(r => r.AccountFk != accountGuid);
            if (mismatchCount > 0)
                logger.LogWarning("sync/goal-progress account={Account} skipped {Skipped} records with mismatched AccountFk",
                    accountGuid[..8], mismatchCount);

            var incomingGuids = req.Records.Select(r => r.Guid).ToList();
            var existingMap = await db.GoalProgresses
                .Where(p => incomingGuids.Contains(p.Guid))
                .ToDictionaryAsync(p => p.Guid);
            foreach (var dto in req.Records)
            {
                if (dto.AccountFk != accountGuid) continue;
                if (!existingMap.TryGetValue(dto.Guid, out var entity))
                    db.GoalProgresses.Add(DtoToEntity(dto));
                else if (dto.UpdatedOn > entity.UpdatedOn) ApplyDto(entity, dto);
            }
            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
            var delta = await db.GoalProgresses
                .Where(p => p.AccountFk == accountGuid && p.UpdatedOn > req.LastSyncAt)
                .OrderBy(p => p.UpdatedOn)
                .Select(p => EntityToDto(p)).ToListAsync();
            logger.LogDebug("sync/goal-progress account={Account} incoming={Incoming} delta={Delta}",
                accountGuid[..8], req.Records.Count, delta.Count);
            return Results.Ok(new SyncResponse<GoalProgressDto>(delta));
        }).RequireAuthorization();
    }

    private static GoalProgress DtoToEntity(GoalProgressDto dto) => new()
    {
        Guid = dto.Guid, AccountFk = dto.AccountFk, GoalFk = dto.GoalFk,
        NextStepItems = dto.NextStepItems, NextMeetingDate = dto.NextMeetingDate,
        UpdatedOn = dto.UpdatedOn, DeletedAt = dto.DeletedAt
    };
    private static void ApplyDto(GoalProgress e, GoalProgressDto dto)
    {
        e.NextStepItems = dto.NextStepItems; e.NextMeetingDate = dto.NextMeetingDate;
        e.UpdatedOn = dto.UpdatedOn; e.DeletedAt = dto.DeletedAt;
    }
    private static GoalProgressDto EntityToDto(GoalProgress p) => new(
        p.Guid, p.AccountFk, p.GoalFk, p.NextStepItems, p.NextMeetingDate, p.UpdatedOn, p.DeletedAt);
}
