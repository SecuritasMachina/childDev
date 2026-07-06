using LevelUp.Models;
using LevelUp.Services;
using SQLite;

namespace LevelUp.Data;

/// <summary>
/// Owns the encrypted (SQLCipher) local SQLite connection.
///
/// The per-device key lives in MAUI SecureStorage, which on Android is backed by
/// androidx.security EncryptedSharedPreferences + Tink + the Android Keystore.
/// Touching SecureStorage SYNCHRONOUSLY on the UI thread at startup (sync-over-async)
/// deadlocks during Keystore/Tink init and hangs the app on the splash screen
/// (Google Play rejection, .NET 9). So the key fetch, the plaintext->encrypted
/// migration, and opening the connection are all deferred into <see cref="InitAsync"/>,
/// which runs off the UI thread. Nothing here blocks a thread on async work.
/// </summary>
public class LocalDatabase
{
    private readonly string _dbPath;
    private readonly IDbKeyProvider _keyProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SQLiteAsyncConnection? _db;

    public LocalDatabase(string dbPath, IDbKeyProvider keyProvider)
    {
        _dbPath = dbPath;
        _keyProvider = keyProvider;
    }

    /// <summary>The open connection. Only valid after <see cref="InitAsync"/> has completed;
    /// throws otherwise (repositories are only resolved post-splash, after init).</summary>
    public SQLiteAsyncConnection Connection =>
        _db ?? throw new InvalidOperationException(
            "LocalDatabase used before InitAsync() completed. The DB is initialised asynchronously at startup.");

    /// <summary>Idempotently fetches the key, ensures the file is SQLCipher-encrypted, opens the
    /// connection, and creates tables. Safe to call more than once; concurrent callers are serialised.</summary>
    public async Task InitAsync(CancellationToken cancellationToken = default)
    {
        var db = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await db.CreateTableAsync<Account>().ConfigureAwait(false);
        await db.CreateTableAsync<Journal>().ConfigureAwait(false);
        await db.CreateTableAsync<Goal>().ConfigureAwait(false);
        await db.CreateTableAsync<GoalProgress>().ConfigureAwait(false);
        await db.CreateTableAsync<Todo>().ConfigureAwait(false);
        await db.CreateTableAsync<Reminder>().ConfigureAwait(false);
    }

    /// <summary>Lazily creates the encrypted connection on first use (key fetch + migration + open).
    /// All async — never blocks a thread on SecureStorage/Keystore.</summary>
    public async Task<SQLiteAsyncConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_db is not null) return _db;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_db is null)
            {
                var key = await _keyProvider.GetKeyAsync().ConfigureAwait(false);

                // Migrate a legacy plaintext DB into an encrypted one (preserves identity/credentials);
                // falls back to a clean wipe only if migration fails outright. File I/O + SQLite — cheap,
                // off the UI thread.
                DbMigrationGuard.EnsureEncrypted(_dbPath, key);

                SQLitePCL.Batteries_V2.Init();
                var options = new SQLiteConnectionString(_dbPath, storeDateTimeAsTicks: true, key: key);
                _db = new SQLiteAsyncConnection(options);
            }
        }
        finally
        {
            _gate.Release();
        }

        return _db;
    }
}
