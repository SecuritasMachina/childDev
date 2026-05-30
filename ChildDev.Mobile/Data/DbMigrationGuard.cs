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

        // 2. Already encrypted with this key? Retry to absorb transient file locks so we never
        //    misclassify a valid encrypted DB as needing migration/wipe.
        if (TryProbeEncryptedWithRetry(path, key))
            return DbMigrationOutcome.AlreadyEncrypted;

        // 3. Only treat the file as legacy plaintext — and open it UNKEYED for export — when its
        //    bytes actually carry the plaintext SQLite header. This prevents wiping a genuine
        //    encrypted DB that merely failed to open (e.g. SecureStorage key rotation, transient IO).
        var temp = path + ".enc-migrate";
        if (IsPlaintextSqlite(path))
        {
            try
            {
                return AttemptExportMigration(path, key, temp);
            }
            catch
            {
                // Genuinely plaintext but export failed (corrupt source / disk) — wipe so the app starts fresh.
                BestEffortDelete(path, path + "-wal", path + "-shm", temp, temp + "-wal", temp + "-shm");
                return DbMigrationOutcome.Wiped;
            }
        }

        // 4. File is neither openable with the key nor a plaintext SQLite DB: an encrypted DB whose
        //    key is lost/rotated. Synced data is server-recoverable, so wipe is the only recovery —
        //    but we reach here only after the keyed-probe retries failed, not on a single transient error.
        BestEffortDelete(path, path + "-wal", path + "-shm", temp, temp + "-wal", temp + "-shm");
        return DbMigrationOutcome.Wiped;
    }

    // Standard SQLite file header (first 16 bytes) — absent in SQLCipher-encrypted files (encrypted).
    private static readonly byte[] SqliteMagic = System.Text.Encoding.ASCII.GetBytes("SQLite format 3\0");

    private static bool IsPlaintextSqlite(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var header = new byte[16];
            if (fs.Read(header, 0, 16) < 16) return false;
            return header.AsSpan().SequenceEqual(SqliteMagic);
        }
        catch { return false; }
    }

    private static bool TryProbeEncryptedWithRetry(string path, string key)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (TryProbeEncrypted(path, key)) return true;
            if (attempt < 2) Thread.Sleep(100);
        }
        return false;
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
