using System.Security.Claims;
using ChildDev.Api.Data;
using ChildDev.Api.Models.Dtos;
using ChildDev.Api.Models.Entities;
using ChildDev.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Endpoints;

public static class TodoEndpoints
{
    public static void MapTodoEndpoints(this WebApplication app)
    {
        app.MapPost("/api/sync/todo", async (
            SyncRequest<TodoDto> req, ClaimsPrincipal user, AppDbContext db, JwtService jwt) =>
        {
            var accountGuid = jwt.ExtractAccountGuid(user);
            if (accountGuid is null) return Results.Unauthorized();
            if (req.Records is null) return Results.BadRequest("Records must not be null.");
            if (req.Records.Count > 500) return Results.BadRequest("Records must not exceed 500 per sync.");
            var maxFutureMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 300_000;
            if (req.Records.Any(r => r.UpdatedOn > maxFutureMs))
                return Results.UnprocessableEntity("Record UpdatedOn is too far in the future.");
            var incomingGuids = req.Records.Select(r => r.Guid).ToList();
            var existingMap = await db.Todos
                .Where(t => incomingGuids.Contains(t.Guid))
                .ToDictionaryAsync(t => t.Guid);
            foreach (var dto in req.Records)
            {
                if (dto.AccountFk != accountGuid) continue;
                if (!existingMap.TryGetValue(dto.Guid, out var entity))
                    db.Todos.Add(DtoToEntity(dto));
                else if (dto.UpdatedOn > entity.UpdatedOn) ApplyDto(entity, dto);
            }
            await db.SaveChangesAsync();
            var delta = await db.Todos
                .Where(t => t.AccountFk == accountGuid && t.UpdatedOn > req.LastSyncAt)
                .OrderBy(t => t.UpdatedOn)
                .Select(t => EntityToDto(t)).ToListAsync();
            return Results.Ok(new SyncResponse<TodoDto>(delta));
        }).RequireAuthorization();
    }

    private static Todo DtoToEntity(TodoDto dto) => new()
    {
        Guid = dto.Guid, AccountFk = dto.AccountFk, Title = dto.Title,
        Notes = dto.Notes, DueDate = dto.DueDate, CompletedAt = dto.CompletedAt,
        UpdatedOn = dto.UpdatedOn, DeletedAt = dto.DeletedAt
    };
    private static void ApplyDto(Todo e, TodoDto dto)
    {
        e.Title = dto.Title; e.Notes = dto.Notes; e.DueDate = dto.DueDate;
        e.CompletedAt = dto.CompletedAt; e.UpdatedOn = dto.UpdatedOn; e.DeletedAt = dto.DeletedAt;
    }
    private static TodoDto EntityToDto(Todo t) => new(
        t.Guid, t.AccountFk, t.Title, t.Notes, t.DueDate, t.CompletedAt, t.UpdatedOn, t.DeletedAt);
}
