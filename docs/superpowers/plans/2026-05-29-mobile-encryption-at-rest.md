# Mobile Encryption at Rest (SQLCipher) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Encrypt the entire local MAUI SQLite database with SQLCipher, keyed from a per-device key stored in MAUI `SecureStorage`, with a wipe+resync migration for already-deployed plaintext databases.

**Architecture:** Replace the `sqlite-net-pcl` + `bundle_green` provider with `sqlite-net-sqlcipher`. `LocalDatabase` opens the connection with a passphrase obtained from an `IDbKeyProvider` (MAUI SecureStorage in-app; a deterministic fallback on the `NO_MAUI`/net8.0 test target). On first encrypted launch, the legacy plaintext DB is deleted and data is re-pulled via the existing sync service.

**Tech Stack:** .NET MAUI (net8.0-android + net8.0), `sqlite-net-sqlcipher`, MAUI `SecureStorage`, xUnit (`ChildDev.Mobile.Tests`).

**Spec:** `docs/superpowers/specs/2026-05-29-mobile-encryption-at-rest-design.md`

**Build/test command (verified):**
`MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true`

**Verified facts:**
- `LocalDatabase` (`ChildDev.Mobile/Data/LocalDatabase.cs`) wraps `SQLiteAsyncConnection` and is constructed manually in `MauiProgram.cs:28` with a `dbPath` string, then registered as a singleton along with `localDb.Connection`.
- Packages today: `sqlite-net-pcl` 1.9.172 + `SQLitePCLRaw.bundle_green` 2.1.10 (`LevelUp.csproj:62-63`).
- Test target strips MAUI via `NO_MAUI` (`LevelUp.csproj:69-71`); `SqliteFixture` only calls `SQLitePCL.Batteries_V2.Init()`.

---

## File Structure

- Modify `ChildDev.Mobile/LevelUp.csproj` — swap SQLite packages.
- Create `ChildDev.Mobile/Services/IDbKeyProvider.cs` — key abstraction.
- Create `ChildDev.Mobile/Services/SecureStorageDbKeyProvider.cs` — MAUI impl (guarded `#if !NO_MAUI`).
- Create `ChildDev.Mobile/Services/InMemoryDbKeyProvider.cs` — non-MAUI/test fallback.
- Modify `ChildDev.Mobile/Data/LocalDatabase.cs` — open keyed connection.
- Modify `ChildDev.Mobile/MauiProgram.cs` — resolve key, build `LocalDatabase`, run migration.
- Create `ChildDev.Mobile/Data/DbMigrationGuard.cs` — wipe-legacy-plaintext logic.
- Tests in `ChildDev.Mobile.Tests/`.

---

## Task 1: Swap SQLite packages to SQLCipher

**Files:**
- Modify: `ChildDev.Mobile/LevelUp.csproj:61-67`

- [ ] **Step 1: Replace the shared SQLite package group**

Change the always-on ItemGroup (`LevelUp.csproj:61-67`) from:
```xml
		<PackageReference Include="sqlite-net-pcl" Version="1.9.172" />
		<PackageReference Include="SQLitePCLRaw.bundle_green" Version="2.1.10" />
```
to:
```xml
		<PackageReference Include="sqlite-net-sqlcipher" Version="1.9.172" />
```
Leave `CommunityToolkit.Mvvm`, `BCrypt.Net-Next`, `Microsoft.Extensions.Http` unchanged.

> `sqlite-net-sqlcipher` transitively pulls `SQLitePCLRaw.bundle_e_sqlcipher`. `bundle_green` MUST be removed — having both causes a native provider conflict.

- [ ] **Step 2: Restore + build the test target**

Run: `MSBuildEnableWorkloadResolver=false dotnet build ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true`
Expected: restores `sqlite-net-sqlcipher`, builds. (Existing tests still construct `LocalDatabase` with a path — next tasks add the key.)

- [ ] **Step 3: Commit**

```bash
git add ChildDev.Mobile/LevelUp.csproj
git commit -m "build(mobile): swap sqlite-net-pcl/bundle_green for sqlite-net-sqlcipher"
```

---

## Task 2: IDbKeyProvider + fallback

**Files:**
- Create: `ChildDev.Mobile/Services/IDbKeyProvider.cs`
- Create: `ChildDev.Mobile/Services/InMemoryDbKeyProvider.cs`
- Create: `ChildDev.Mobile/Services/SecureStorageDbKeyProvider.cs`
- Test: `ChildDev.Mobile.Tests/DbKeyProviderTests.cs`

- [ ] **Step 1: Write the failing test (fallback provider)**

```csharp
using LevelUp.Services;
using Xunit;

namespace LevelUp.Tests;

public class DbKeyProviderTests
{
    [Fact]
    public async Task InMemoryProvider_ReturnsStableNonEmptyKey()
    {
        IDbKeyProvider p = new InMemoryDbKeyProvider();
        var k1 = await p.GetKeyAsync();
        var k2 = await p.GetKeyAsync();
        Assert.False(string.IsNullOrWhiteSpace(k1));
        Assert.Equal(k1, k2); // stable within a process
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true --filter DbKeyProviderTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement**

`IDbKeyProvider.cs`:
```csharp
namespace LevelUp.Services;

/// <summary>Supplies the SQLCipher passphrase for the local database.</summary>
public interface IDbKeyProvider
{
    Task<string> GetKeyAsync();
}
```

`InMemoryDbKeyProvider.cs` (used on the NO_MAUI/test target and as a non-secure fallback):
```csharp
using System.Security.Cryptography;

namespace LevelUp.Services;

/// <summary>Non-persistent key provider for tests / NO_MAUI builds. NOT for production devices.</summary>
public sealed class InMemoryDbKeyProvider : IDbKeyProvider
{
    private readonly string _key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public Task<string> GetKeyAsync() => Task.FromResult(_key);
}
```

`SecureStorageDbKeyProvider.cs` (MAUI only):
```csharp
#if !NO_MAUI
using System.Security.Cryptography;
using Microsoft.Maui.Storage;

namespace LevelUp.Services;

/// <summary>Stores/loads a per-device SQLCipher key in MAUI SecureStorage (Android Keystore-backed).</summary>
public sealed class SecureStorageDbKeyProvider : IDbKeyProvider
{
    private const string KeyName = "levelup_db_key";

    public async Task<string> GetKeyAsync()
    {
        var existing = await SecureStorage.Default.GetAsync(KeyName);
        if (!string.IsNullOrWhiteSpace(existing)) return existing;

        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await SecureStorage.Default.SetAsync(KeyName, key);
        return key;
    }
}
#endif
```

- [ ] **Step 4: Run to verify it passes**

Run: `MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true --filter DbKeyProviderTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ChildDev.Mobile/Services/IDbKeyProvider.cs ChildDev.Mobile/Services/InMemoryDbKeyProvider.cs ChildDev.Mobile/Services/SecureStorageDbKeyProvider.cs ChildDev.Mobile.Tests/DbKeyProviderTests.cs
git commit -m "feat(mobile): IDbKeyProvider with SecureStorage impl + test fallback"
```

---

## Task 3: LocalDatabase opens a keyed (encrypted) connection

**Files:**
- Modify: `ChildDev.Mobile/Data/LocalDatabase.cs`
- Test: `ChildDev.Mobile.Tests/EncryptedLocalDatabaseTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using LevelUp.Data;
using LevelUp.Models;
using Xunit;

namespace LevelUp.Tests;

public class EncryptedLocalDatabaseTests
{
    public EncryptedLocalDatabaseTests() => SqliteFixture.EnsureInit();

    [Fact]
    public async Task DataRoundTrips_WithKey()
    {
        var path = Path.Combine(Path.GetTempPath(), $"enc_{Guid.NewGuid():N}.db3");
        try
        {
            var db = new LocalDatabase(path, "TXk0NEJ5dGVLZXlNdXN0QmUzMkJ5dGVzTG9uZ0FBQQ==");
            await db.InitAsync();
            await db.Connection.InsertAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = "A", GoalText = "x", UpdatedOn = 1 });
            var count = await db.Connection.Table<Goal>().CountAsync();
            Assert.Equal(1, count);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task WrongKey_CannotRead()
    {
        var path = Path.Combine(Path.GetTempPath(), $"enc_{Guid.NewGuid():N}.db3");
        try
        {
            var good = new LocalDatabase(path, "Z29vZGtleWdvb2RrZXlnb29ka2V5Z29vZGtleTEy");
            await good.InitAsync();
            await good.Connection.InsertAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = "A", GoalText = "x", UpdatedOn = 1 });
            await good.Connection.CloseAsync();

            var bad = new LocalDatabase(path, "YmFka2V5YmFka2V5YmFka2V5YmFka2V5YmFka2V5MTI=");
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await bad.InitAsync();
                await bad.Connection.Table<Goal>().CountAsync();
            });
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true --filter EncryptedLocalDatabaseTests`
Expected: FAIL — `LocalDatabase` has no `(string, string)` constructor.

- [ ] **Step 3: Implement keyed connection**

Replace `LocalDatabase.cs`:
```csharp
using LevelUp.Models;
using SQLite;

namespace LevelUp.Data;

public class LocalDatabase
{
    private readonly SQLiteAsyncConnection _db;

    public LocalDatabase(string dbPath, string key)
    {
        SQLitePCL.Batteries_V2.Init();
        var options = new SQLiteConnectionString(
            dbPath,
            storeDateTimeAsTicks: true,
            key: key);
        _db = new SQLiteAsyncConnection(options);
    }

    public SQLiteAsyncConnection Connection => _db;

    public async Task InitAsync()
    {
        await _db.CreateTableAsync<Account>();
        await _db.CreateTableAsync<Journal>();
        await _db.CreateTableAsync<Goal>();
        await _db.CreateTableAsync<GoalProgress>();
        await _db.CreateTableAsync<Todo>();
        await _db.CreateTableAsync<Reminder>();
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true --filter EncryptedLocalDatabaseTests`
Expected: PASS.

- [ ] **Step 5: Fix existing test/fixture constructors**

Existing repository tests construct `LocalDatabase`. Update each call site to pass a test key. Search:
Run: `grep -rn "new LocalDatabase(" ChildDev.Mobile.Tests`
For each, add a constant test key argument (reuse a shared `SqliteFixture.TestKey`). Add to `SqliteFixture`:
```csharp
public const string TestKey = "VGVzdEtleVRlc3RLZXlUZXN0S2V5VGVzdEtleTEyMzQ=";
```
Then change `new LocalDatabase(path)` → `new LocalDatabase(path, SqliteFixture.TestKey)`.

- [ ] **Step 6: Run the FULL mobile suite**

Run: `MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true`
Expected: ALL pass (repository round-trips now run against an encrypted DB).

- [ ] **Step 7: Commit**

```bash
git add ChildDev.Mobile/Data/LocalDatabase.cs ChildDev.Mobile.Tests/
git commit -m "feat(mobile): open local SQLite via SQLCipher keyed connection; update tests"
```

---

## Task 4: Migration guard (wipe legacy plaintext DB)

**Files:**
- Create: `ChildDev.Mobile/Data/DbMigrationGuard.cs`
- Test: `ChildDev.Mobile.Tests/DbMigrationGuardTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using LevelUp.Data;
using Xunit;

namespace LevelUp.Tests;

public class DbMigrationGuardTests
{
    public DbMigrationGuardTests() => SqliteFixture.EnsureInit();

    [Fact]
    public void DeletesUnopenableLegacyFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"legacy_{Guid.NewGuid():N}.db3");
        File.WriteAllText(path, "not a sqlcipher db"); // simulate plaintext/legacy file
        var wiped = DbMigrationGuard.EnsureOpenableOrWipe(path, SqliteFixture.TestKey);
        Assert.True(wiped);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void KeepsValidEncryptedFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"enc_{Guid.NewGuid():N}.db3");
        try
        {
            var db = new LocalDatabase(path, SqliteFixture.TestKey);
            db.InitAsync().GetAwaiter().GetResult();
            db.Connection.CloseAsync().GetAwaiter().GetResult();
            var wiped = DbMigrationGuard.EnsureOpenableOrWipe(path, SqliteFixture.TestKey);
            Assert.False(wiped);
            Assert.True(File.Exists(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true --filter DbMigrationGuardTests`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Implement**

```csharp
using SQLite;

namespace LevelUp.Data;

/// <summary>Ensures the DB file at <paramref name="path"/> can be opened with the SQLCipher key.
/// A pre-existing unencrypted (legacy) DB cannot be opened with a key, so it is deleted and a
/// fresh encrypted DB will be created + repopulated by sync. Returns true if it wiped a file.</summary>
public static class DbMigrationGuard
{
    public static bool EnsureOpenableOrWipe(string path, string key)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var opts = new SQLiteConnectionString(path, storeDateTimeAsTicks: true, key: key);
            using var probe = new SQLiteConnection(opts);
            // Force a read of the schema; throws if key/format is wrong (legacy plaintext).
            probe.ExecuteScalar<int>("PRAGMA user_version;");
            return false;
        }
        catch
        {
            try { File.Delete(path); } catch { /* best effort */ }
            // also clear -wal/-shm if present
            foreach (var sfx in new[] { "-wal", "-shm" })
                if (File.Exists(path + sfx)) { try { File.Delete(path + sfx); } catch { } }
            return true;
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true --filter DbMigrationGuardTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ChildDev.Mobile/Data/DbMigrationGuard.cs ChildDev.Mobile.Tests/DbMigrationGuardTests.cs
git commit -m "feat(mobile): wipe legacy plaintext DB when it cannot be opened with key"
```

---

## Task 5: Wire MauiProgram (key resolution + guard + resync)

**Files:**
- Modify: `ChildDev.Mobile/MauiProgram.cs:24-30`

- [ ] **Step 1: Replace the DB construction block**

Replace `MauiProgram.cs:24-30` (`var dbPath = …` through the two `AddSingleton(localDb…)` lines) with:
```csharp
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "childdev.db3");

        IDbKeyProvider keyProvider = new SecureStorageDbKeyProvider();
        builder.Services.AddSingleton<IDbKeyProvider>(keyProvider);

        // Resolve the key once at startup (SecureStorage is async; block briefly here).
        var dbKey = keyProvider.GetKeyAsync().GetAwaiter().GetResult();

        // Migrate: a pre-existing plaintext DB can't be opened with the key -> wipe + resync.
        var wiped = DbMigrationGuard.EnsureOpenableOrWipe(dbPath, dbKey);

        var localDb = new LocalDatabase(dbPath, dbKey);
        builder.Services.AddSingleton(localDb);
        builder.Services.AddSingleton(localDb.Connection);
        builder.Services.AddSingleton(new DbFreshState(wiped));
```

Add `using LevelUp.Services;` if not present.

- [ ] **Step 2: Add the DbFreshState marker + trigger resync**

Create `ChildDev.Mobile/Data/DbFreshState.cs`:
```csharp
namespace LevelUp.Data;

/// <summary>True when the local DB was just wiped (legacy plaintext migration), so a full
/// sync pull should run to repopulate from the server.</summary>
public sealed record DbFreshState(bool WasWiped);
```

In the app's existing startup/sync entry point (where `SyncService` first runs after launch — check `App.xaml.cs` / first ViewModel that triggers sync), inject `DbFreshState` and, when `WasWiped` is true, force a full pull (e.g. reset the last-sync timestamp to 0 before the normal sync). Concretely, in the sync trigger:
```csharp
if (_freshState.WasWiped)
    await _syncService.FullPullAsync(); // or: reset lastSyncAt=0 then SyncAsync()
```
If `SyncService` has no `FullPullAsync`, use its existing "first sync" path (last-sync defaults to 0 on a fresh DB, so the normal sync already pulls everything — in that case `DbFreshState` is informational only and no extra call is needed). Verify which applies by reading `SyncService`.

- [ ] **Step 3: Build the test target (compile check of shared code)**

Run: `MSBuildEnableWorkloadResolver=false dotnet build ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true`
Expected: builds.

- [ ] **Step 4: Commit**

```bash
git add ChildDev.Mobile/MauiProgram.cs ChildDev.Mobile/Data/DbFreshState.cs ChildDev.Mobile/
git commit -m "feat(mobile): wire SecureStorage key, legacy-DB wipe, and resync-on-fresh"
```

---

## Task 6: Android device verification (manual)

- [ ] **Step 1: Build the Android APK**

Run: `dotnet build ChildDev.Mobile/LevelUp.csproj -f net8.0-android -c Debug`
Expected: APK builds with the SQLCipher native bundle (no duplicate-class/R8 errors; Debug already sets `AndroidLinkMode=None`).

- [ ] **Step 2: Sideload + smoke test on the real app id**

Install on the device and verify against `levelup.securitasmachina.org` (NOT the stale `com.companyname.childdev.mobile`). Steps: launch → existing plaintext DB is wiped → app re-syncs from API → create a goal → kill + relaunch → data persists (key reused from SecureStorage).

- [ ] **Step 3: Confirm encryption on disk (optional, rooted/emulator)**

Pull `childdev.db3` and confirm `head -c 16` is NOT the plaintext `SQLite format 3\0` header (SQLCipher encrypts the header).

- [ ] **Step 4: Commit any fixes found during device testing.**

---

## Self-Review Notes

- **Spec coverage:** package swap incl. bundle_green removal (T1), SQLCipher keyed open (T3), key in SecureStorage + non-MAUI fallback (T2), wipe+resync migration (T4,T5), tests build on net8.0 target (T2,T3,T4), device verification (T6). All covered.
- **Type consistency:** `LocalDatabase(string path, string key)` used identically in T3, T4, T5, and `SqliteFixture.TestKey` defined in T3 used by T4. `IDbKeyProvider.GetKeyAsync()` consistent across T2/T5.
- **Resync nuance (T5):** on a fresh DB the existing sync's last-sync default (0) already pulls everything; `DbFreshState` is informational unless `SyncService` needs an explicit reset — the executor must read `SyncService` to confirm. Flagged, not assumed.
