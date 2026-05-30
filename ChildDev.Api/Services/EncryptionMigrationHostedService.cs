using ChildDev.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Services;

/// <summary>One-shot, idempotent pass that re-encrypts rows whose Phase-1 columns are still
/// legacy plaintext. Relational-only; no-ops on non-relational providers. Safe to run every startup.</summary>
public sealed class EncryptionMigrationHostedService(
    IServiceProvider services,
    ILogger<EncryptionMigrationHostedService> log) : IHostedService
{
    // (table, keyColumn, valueColumns[]) for Phase-1 encrypted content.
    private static readonly (string Table, string Key, string[] Cols)[] Targets =
    {
        ("Goals", "Guid", new[] { "GoalText", "MeasurableOutcome", "Steps" }),
        ("Journals", "Guid", new[] { "Notes" }),
        ("GoalProgresses", "Guid", new[] { "NextStepItems" }),
        ("Todos", "Guid", new[] { "Notes" }),
    };

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!ctx.Database.IsRelational())
            {
                log.LogInformation("Encryption migration skipped (non-relational provider).");
                return;
            }
            var total = await RunOnceAsync(ctx, ct);
            if (total > 0) log.LogInformation("Encryption migration re-encrypted {Count} rows.", total);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Encryption migration pass failed; will retry next startup.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Finds plaintext rows via raw SQL, loads them through EF (IgnoreQueryFilters),
    /// marks the encrypted properties modified, and SaveChanges (converter encrypts). Returns rows touched.</summary>
    public static async Task<int> RunOnceAsync(AppDbContext ctx, CancellationToken ct)
    {
        var touched = 0;
        foreach (var (table, keyCol, cols) in Targets)
        {
            var wherePlain = string.Join(" OR ", cols.Select(c => $"(`{c}` IS NOT NULL AND `{c}` NOT LIKE 'v1:%')"));
            var sql = $"SELECT `{keyCol}` FROM `{table}` WHERE {wherePlain}";
            var keys = new List<string>();
            var conn = ctx.Database.GetDbConnection();
            await ctx.Database.OpenConnectionAsync(ct);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    keys.Add(reader.GetString(0));
            }
            finally { await ctx.Database.CloseConnectionAsync(); }

            if (keys.Count == 0) continue;
            // Batch to keep the IN-clause and transaction bounded on a large first run.
            const int batchSize = 500;
            for (var i = 0; i < keys.Count; i += batchSize)
                touched += await ReencryptAsync(ctx, table, keys.GetRange(i, Math.Min(batchSize, keys.Count - i)), ct);
        }
        return touched;
    }

    private static async Task<int> ReencryptAsync(AppDbContext ctx, string table, List<string> keys, CancellationToken ct)
    {
        // Load via EF and force a no-op modification so the converter re-writes ciphertext.
        // Setting IsModified = true on an unchanged property still forces EF to write it —
        // and the value converter encrypts on write. That converts plaintext → v1:.
        switch (table)
        {
            case "Goals":
                foreach (var g in await ctx.Goals.IgnoreQueryFilters().Where(x => keys.Contains(x.Guid)).ToListAsync(ct))
                {
                    ctx.Entry(g).Property(p => p.GoalText).IsModified = true;
                    ctx.Entry(g).Property(p => p.MeasurableOutcome).IsModified = true;
                    ctx.Entry(g).Property(p => p.Steps).IsModified = true;
                }
                break;
            case "Journals":
                foreach (var j in await ctx.Journals.IgnoreQueryFilters().Where(x => keys.Contains(x.Guid)).ToListAsync(ct))
                    ctx.Entry(j).Property(p => p.Notes).IsModified = true;
                break;
            case "GoalProgresses":
                foreach (var p2 in await ctx.GoalProgresses.IgnoreQueryFilters().Where(x => keys.Contains(x.Guid)).ToListAsync(ct))
                    ctx.Entry(p2).Property(p => p.NextStepItems).IsModified = true;
                break;
            case "Todos":
                foreach (var t in await ctx.Todos.IgnoreQueryFilters().Where(x => keys.Contains(x.Guid)).ToListAsync(ct))
                    ctx.Entry(t).Property(p => p.Notes).IsModified = true;
                break;
        }
        return await ctx.SaveChangesAsync(ct);
    }
}
