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
        app.MapPost("/api/sync/goal-progress", async (
            SyncRequest<GoalProgressDto> req, ClaimsPrincipal user, AppDbContext db, JwtService jwt) =>
        {
            var accountGuid = jwt.ExtractAccountGuid(user);
            if (accountGuid is null) return Results.Unauthorized();
            if (req.Records is null) return Results.BadRequest("Records must not be null.");
            if (req.Records.Count > 500) return Results.BadRequest("Records must not exceed 500 per sync.");
            var maxFutureMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 300_000;
            if (req.Records.Any(r => r.UpdatedOn > maxFutureMs))
                return Results.UnprocessableEntity("Record UpdatedOn is too far in the future.");
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
            await db.SaveChangesAsync();
            var delta = await db.GoalProgresses
                .Where(p => p.AccountFk == accountGuid && p.UpdatedOn > req.LastSyncAt)
                .OrderBy(p => p.UpdatedOn)
                .Select(p => EntityToDto(p)).ToListAsync();
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
