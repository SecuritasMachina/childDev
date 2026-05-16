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
        app.MapPost("/api/sync/goal", async (
            SyncRequest<GoalDto> req, ClaimsPrincipal user, AppDbContext db, JwtService jwt) =>
        {
            var accountGuid = jwt.ExtractAccountGuid(user);
            if (accountGuid is null) return Results.Unauthorized();
            foreach (var dto in req.Records)
            {
                if (dto.AccountFk != accountGuid) continue;
                var existing = await db.Goals.FindAsync(dto.Guid);
                if (existing is null) db.Goals.Add(DtoToEntity(dto));
                else if (dto.UpdatedOn > existing.UpdatedOn) ApplyDto(existing, dto);
            }
            await db.SaveChangesAsync();
            var delta = await db.Goals
                .Where(g => g.AccountFk == accountGuid && g.UpdatedOn > req.LastSyncAt)
                .Select(g => EntityToDto(g)).ToListAsync();
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
