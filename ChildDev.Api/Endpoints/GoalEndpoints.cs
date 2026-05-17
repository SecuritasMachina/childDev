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
                return Results.Problem("Record UpdatedOn is too far in the future.", statusCode: 422);
            if (req.Records.Any(r => !Guid.TryParse(r.Guid, out _)))
                return Results.Problem("Record Guid is not a valid GUID.", statusCode: 422);
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
            await db.SaveChangesAsync();
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
        CompletionDate = dto.CompletionDate, UpdatedOn = dto.UpdatedOn, DeletedAt = dto.DeletedAt
    };
    private static void ApplyDto(Goal e, GoalDto dto)
    {
        e.GoalText = dto.GoalText; e.NextMeetingDate = dto.NextMeetingDate;
        e.ExpirationDate = dto.ExpirationDate; e.MeasurableOutcome = dto.MeasurableOutcome;
        e.CompletionDate = dto.CompletionDate; e.UpdatedOn = dto.UpdatedOn; e.DeletedAt = dto.DeletedAt;
    }
    private static GoalDto EntityToDto(Goal g) => new(
        g.Guid, g.AccountFk, g.GoalText, g.NextMeetingDate, g.ExpirationDate,
        g.EnteredDate, g.MeasurableOutcome, g.CompletionDate, g.UpdatedOn, g.DeletedAt);
}
