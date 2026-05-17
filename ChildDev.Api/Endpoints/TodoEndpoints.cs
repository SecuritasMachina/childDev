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
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("sync.todo");
        app.MapPost("/api/sync/todo", async (
            SyncRequest<TodoDto> req, ClaimsPrincipal user, AppDbContext db, JwtService jwt) =>
        {
            var accountGuid = jwt.ExtractAccountGuid(user);
            if (accountGuid is null) return Results.Unauthorized();
            if (req.Records is null) return Results.Problem("Records must not be null.", statusCode: 400);
            if (req.Records.Count > 500) return Results.Problem("Records must not exceed 500 per sync.", statusCode: 400);
            var maxFutureMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 300_000;
            if (req.Records.Any(r => r.UpdatedOn > maxFutureMs))
            {
                logger.LogWarning("sync/todo account={Account} rejected: future UpdatedOn", accountGuid[..8]);
                return Results.Problem("Record UpdatedOn is too far in the future.", statusCode: 422);
            }
            if (req.Records.Any(r => !Guid.TryParse(r.Guid, out _)))
            {
                logger.LogWarning("sync/todo account={Account} rejected: invalid Guid", accountGuid[..8]);
                return Results.Problem("Record Guid is not a valid GUID.", statusCode: 422);
            }
            var maxFutureTimestampMs = DateTimeOffset.UtcNow.AddYears(10).ToUnixTimeMilliseconds();
            if (req.Records.Any(r => r.DueDate.HasValue && r.DueDate.Value > maxFutureTimestampMs))
            {
                logger.LogWarning("sync/todo account={Account} rejected: DueDate too far in future", accountGuid[..8]);
                return Results.Problem("Record DueDate is too far in the future.", statusCode: 422);
            }
            if (req.Records.Any(r => r.CompletedAt.HasValue && r.CompletedAt.Value > maxFutureTimestampMs))
            {
                logger.LogWarning("sync/todo account={Account} rejected: CompletedAt too far in future", accountGuid[..8]);
                return Results.Problem("Record CompletedAt is too far in the future.", statusCode: 422);
            }
            if (req.Records.Any(r => r.Title?.Length > 500))
            {
                logger.LogWarning("sync/todo account={Account} rejected: Title too long", accountGuid[..8]);
                return Results.Problem("Record Title must not exceed 500 characters.", statusCode: 422);
            }
            if (req.Records.Any(r => r.Notes?.Length > 2_000))
            {
                logger.LogWarning("sync/todo account={Account} rejected: Notes too long", accountGuid[..8]);
                return Results.Problem("Record Notes must not exceed 2000 characters.", statusCode: 422);
            }
            if (req.Records.Any(r => r.DeletedAt is null && r.CompletedAt is null && string.IsNullOrWhiteSpace(r.Title)))
            {
                logger.LogWarning("sync/todo account={Account} rejected: blank Title", accountGuid[..8]);
                return Results.Problem("Record Title must not be blank.", statusCode: 422);
            }
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
            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
            var delta = await db.Todos
                .Where(t => t.AccountFk == accountGuid && t.UpdatedOn > req.LastSyncAt)
                .OrderBy(t => t.UpdatedOn)
                .Select(t => EntityToDto(t)).ToListAsync();
            logger.LogDebug("sync/todo account={Account} incoming={Incoming} delta={Delta}",
                accountGuid[..8], req.Records.Count, delta.Count);
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
