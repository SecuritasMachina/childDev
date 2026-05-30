using SQLite;

namespace LevelUp.Data;

public enum DbMigrationOutcome { None, AlreadyEncrypted, Migrated, Wiped }

/// <summary>
/// Ensures the local SQLite database is SQLCipher-encrypted.
/// Export-primary, wipe-fallback: copies data from a legacy plaintext DB into
/// a new encrypted DB via sqlcipher_export (preserving identity/credentials).
/// If that fails for any reason, wipes so the app can start fresh.
/// </summary>
public static class DbMigrationGuard
{
    /// <summary>Ensures the DB at <paramref name="path"/> is SQLCipher-encrypted with <paramref name="key"/>.
    /// If a legacy PLAINTEXT DB exists, copies its data into a new encrypted DB via sqlcipher_export
    /// (preserving identity/credentials). If that fails, wipes so the app never bricks on launch.</summary>
    public static DbMigrationOutcome EnsureEncrypted(string path, string key)
    {
        // 1. No file — nothing to do.
        if (!File.Exists(path))
            return DbMigrationOutcome.None;

        // 2. Check if already encrypted by trying to open with the key.
        if (TryProbeEncrypted(path, key))
            return DbMigrationOutcome.AlreadyEncrypted;

        // 3. Legacy plaintext (or unreadable) DB — attempt export migration.
        var temp = path + ".enc-migrate";
        try
        {
            return AttemptExportMigration(path, key, temp);
        }
        catch
        {
            // Wipe fallback — best-effort delete all related files.
            BestEffortDelete(path, path + "-wal", path + "-shm", temp, temp + "-wal", temp + "-shm");
            return DbMigrationOutcome.Wiped;
        }
    }

    private static bool TryProbeEncrypted(string path, string key)
    {
        try
        {
            var cs = new SQLiteConnectionString(path, storeDateTimeAsTicks: true, key: key);
            using var conn = new SQLiteConnection(cs);
            // A successful read proves the DB is already encrypted with this key.
            conn.ExecuteScalar<int>("PRAGMA user_version;");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static DbMigrationOutcome AttemptExportMigration(string path, string key, string temp)
    {
        // Clean up any leftover temp files from a previous failed attempt.
        BestEffortDelete(temp, temp + "-wal", temp + "-shm");

        // Open the old DB unkeyed (plaintext).
        var oldCs = new SQLiteConnectionString(path, storeDateTimeAsTicks: true, key: null);
        using (var old = new SQLiteConnection(oldCs))
        {
            // Escape single quotes in key and temp path (defensive; base64 keys won't have them).
            var k = key.Replace("'", "''");
            var t = temp.Replace("'", "''");

            old.Execute($"ATTACH DATABASE '{t}' AS enc KEY '{k}';");
            old.ExecuteScalar<string>("SELECT sqlcipher_export('enc');");
            old.Execute("DETACH DATABASE enc;");
        }

        // Verify the exported file opens correctly with the key.
        if (!TryProbeEncrypted(temp, key))
            throw new InvalidOperationException("sqlcipher_export verification failed: temp file cannot be opened with the provided key.");

        // Replace old DB with the new encrypted one.
        BestEffortDelete(path, path + "-wal", path + "-shm");
        File.Move(temp, path);

        // Move temp -wal/-shm if they exist (export usually checkpoints, but be safe).
        if (File.Exists(temp + "-wal")) File.Move(temp + "-wal", path + "-wal", overwrite: true);
        if (File.Exists(temp + "-shm")) File.Move(temp + "-shm", path + "-shm", overwrite: true);

        return DbMigrationOutcome.Migrated;
    }

    private static void BestEffortDelete(params string[] paths)
    {
        foreach (var p in paths)
        {
            try { if (File.Exists(p)) File.Delete(p); } catch { /* ignore */ }
        }
    }
}
