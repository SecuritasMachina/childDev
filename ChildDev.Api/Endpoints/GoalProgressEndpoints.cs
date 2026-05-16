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
            foreach (var dto in req.Records)
            {
                if (dto.AccountFk != accountGuid) continue;
                var existing = await db.GoalProgresses.FindAsync(dto.Guid);
                if (existing is null) db.GoalProgresses.Add(DtoToEntity(dto));
                else if (dto.UpdatedOn > existing.UpdatedOn) ApplyDto(existing, dto);
            }
            await db.SaveChangesAsync();
            var delta = await db.GoalProgresses
                .Where(p => p.AccountFk == accountGuid && p.UpdatedOn > req.LastSyncAt)
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
