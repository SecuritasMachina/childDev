using System.Net.Http.Headers;
using System.Net.Http.Json;
using LevelUp.Data;
using LevelUp.Models;

namespace LevelUp.Services;

public enum SyncResult { Success, NoServer, Failed }

public class SyncService(
    JournalRepository journalRepo,
    GoalRepository goalRepo,
    GoalProgressRepository goalProgressRepo,
    TodoRepository todoRepo,
    AccountService accountService,
    ConnectivityService connectivity,
    IHttpClientFactory httpFactory)
{
    private int _syncing;

    public async Task<SyncResult> RunAsync(Account account)
    {
        if (!connectivity.IsConnected) return SyncResult.NoServer;
        if (string.IsNullOrEmpty(account.ServerUrl) || string.IsNullOrEmpty(account.ServerJwt))
            return SyncResult.NoServer;
        if (Interlocked.CompareExchange(ref _syncing, 1, 0) == 1) return SyncResult.Success;

        try
        {
            var client = httpFactory.CreateClient("childdev");
            client.BaseAddress = new Uri(account.ServerUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", account.ServerJwt);

            // Pre-flight with a short deadline — if the server doesn't respond in 5s it's unreachable
            using var healthCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            HttpResponseMessage ping;
            try
            {
                ping = await client.GetAsync("health", healthCts.Token);
            }
            catch (OperationCanceledException)
            {
                return SyncResult.NoServer;
            }
            if (!ping.IsSuccessStatusCode) return SyncResult.NoServer;

            // Capture start time before any entity syncs so records modified during
            // the sync window (T_start..T_end) are picked up on the next sync.
            var syncStartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var since = account.LastSyncAt;

            await SyncEntityAsync<Journal, JournalSyncDto>(
                client, "sync/journal", since,
                () => journalRepo.GetModifiedSinceAsync(account.Guid, since),
                j => new JournalSyncDto(j.Guid, j.AccountFk, j.Notes, j.Activity, j.Mood, j.EmotionReason, j.Tags,
                    j.EnteredDate, j.UpdatedOn, j.DeletedAt),
                dto => journalRepo.UpsertFromSyncAsync(new Journal
                {
                    Guid = dto.Guid, AccountFk = dto.AccountFk, Notes = dto.Notes,
                    Activity = dto.Activity, Mood = dto.Mood, EmotionReason = dto.EmotionReason,
                    Tags = dto.Tags, EnteredDate = dto.EnteredDate, UpdatedOn = dto.UpdatedOn, DeletedAt = dto.DeletedAt
                }));

            await SyncEntityAsync<Goal, GoalSyncDto>(
                client, "sync/goal", since,
                () => goalRepo.GetModifiedSinceAsync(account.Guid, since),
                g => new GoalSyncDto(g.Guid, g.AccountFk, g.GoalText, g.NextMeetingDate,
                    g.ExpirationDate, g.EnteredDate, g.MeasurableOutcome, g.CompletionDate, g.UpdatedOn, g.DeletedAt,
                    g.ProgressPercent, g.Category, g.IsPinned),
                dto => goalRepo.UpsertFromSyncAsync(new Goal
                {
                    Guid = dto.Guid, AccountFk = dto.AccountFk, GoalText = dto.GoalText,
                    NextMeetingDate = dto.NextMeetingDate, ExpirationDate = dto.ExpirationDate,
                    EnteredDate = dto.EnteredDate, MeasurableOutcome = dto.MeasurableOutcome,
                    CompletionDate = dto.CompletionDate, UpdatedOn = dto.UpdatedOn, DeletedAt = dto.DeletedAt,
                    ProgressPercent = dto.ProgressPercent, Category = dto.Category, IsPinned = dto.IsPinned
                }));

            await SyncEntityAsync<GoalProgress, GoalProgressSyncDto>(
                client, "sync/goal-progress", since,
                () => goalProgressRepo.GetModifiedSinceAsync(account.Guid, since),
                p => new GoalProgressSyncDto(p.Guid, p.AccountFk, p.GoalFk, p.NextStepItems,
                    p.NextMeetingDate, p.UpdatedOn, p.DeletedAt),
                dto => goalProgressRepo.UpsertFromSyncAsync(new GoalProgress
                {
                    Guid = dto.Guid, AccountFk = dto.AccountFk, GoalFk = dto.GoalFk,
                    NextStepItems = dto.NextStepItems, NextMeetingDate = dto.NextMeetingDate,
                    UpdatedOn = dto.UpdatedOn, DeletedAt = dto.DeletedAt
                }));

            await SyncEntityAsync<Todo, TodoSyncDto>(
                client, "sync/todo", since,
                () => todoRepo.GetModifiedSinceAsync(account.Guid, since),
                t => new TodoSyncDto(t.Guid, t.AccountFk, t.Title, t.Notes, t.DueDate,
                    t.CompletedAt, t.UpdatedOn, t.DeletedAt),
                dto => todoRepo.UpsertFromSyncAsync(new Todo
                {
                    Guid = dto.Guid, AccountFk = dto.AccountFk, Title = dto.Title,
                    Notes = dto.Notes, DueDate = dto.DueDate, CompletedAt = dto.CompletedAt,
                    UpdatedOn = dto.UpdatedOn, DeletedAt = dto.DeletedAt
                }));

            await accountService.UpdateLastSyncAsync(syncStartedAt);
            return SyncResult.Success;
        }
        catch
        {
            return SyncResult.Failed;
        }
        finally
        {
            Interlocked.Exchange(ref _syncing, 0);
        }
    }

    private static async Task SyncEntityAsync<TLocal, TDto>(
        HttpClient client,
        string endpoint,
        long lastSyncAt,
        Func<Task<List<TLocal>>> getLocalModified,
        Func<TLocal, TDto> toDto,
        Func<TDto, Task> upsertLocal)
    {
        var localModified = await getLocalModified();
        var dtos = localModified.Select(toDto).ToList();

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(endpoint, new SyncRequestDto<TDto>(dtos, lastSyncAt));
            if ((int)response.StatusCode >= 500)
                response = await client.PostAsJsonAsync(endpoint, new SyncRequestDto<TDto>(dtos, lastSyncAt));
        }
        catch (HttpRequestException)
        {
            response = await client.PostAsJsonAsync(endpoint, new SyncRequestDto<TDto>(dtos, lastSyncAt));
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SyncResponseDto<TDto>>();
        if (result?.Records is null) return;

        foreach (var dto in result.Records)
            await upsertLocal(dto);
    }
}

// Local DTO types matching the API's SyncDtos exactly
public record SyncRequestDto<T>(List<T> Records, long LastSyncAt);
public record SyncResponseDto<T>(List<T> Records);

public record JournalSyncDto(string Guid, string AccountFk, string? Notes, string? Activity,
    string? Mood, string? EmotionReason, string? Tags, long EnteredDate, long UpdatedOn, long? DeletedAt);

public record GoalSyncDto(string Guid, string AccountFk, string? GoalText,
    long? NextMeetingDate, long? ExpirationDate, long EnteredDate, string? MeasurableOutcome,
    long? CompletionDate, long UpdatedOn, long? DeletedAt,
    int? ProgressPercent = null, string? Category = null, bool IsPinned = false);

public record GoalProgressSyncDto(string Guid, string AccountFk, string GoalFk,
    string? NextStepItems, long? NextMeetingDate, long UpdatedOn, long? DeletedAt);

public record TodoSyncDto(string Guid, string AccountFk, string? Title, string? Notes,
    long? DueDate, long? CompletedAt, long UpdatedOn, long? DeletedAt);
