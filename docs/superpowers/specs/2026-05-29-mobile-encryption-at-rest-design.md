# Mobile Tier — Encryption at Rest (Design)

**Date:** 2026-05-29
**Project:** ChildDev.Mobile (.NET MAUI, offline-first LWW sync, local SQLite)
**Scope:** Mobile tier only. Web/API tier is a separate spec
(`2026-05-29-web-encryption-and-tenant-isolation-design.md`).

## Goal

Encrypt the entire local SQLite database at rest so that a device backup, file
pull, or stolen/rooted device does not expose user content in plaintext.

## Non-Goals

- No field-level encryption (full-DB SQLCipher covers everything).
- No change to the sync protocol or LWW semantics.
- No change to data shape (timestamps, FKs, soft deletes stay as-is).

## Current State (verified)

- `LocalDatabase.cs` opens a **plaintext** SQLite DB via `sqlite-net-pcl` +
  `SQLitePCLRaw.bundle_green`. No `SecureStorage` usage anywhere.
- The `net8.0` (`SkipMauiTargets` / `NO_MAUI`) test target strips MAUI; mobile
  tests (`SqliteFixture`) construct `LocalDatabase` directly without MAUI APIs.

## Approach — SQLCipher full-database encryption

### Packages

- **Remove** `SQLitePCLRaw.bundle_green`.
- **Add** `sqlite-net-sqlcipher` (pulls the SQLCipher native bundle).
  `bundle_green` and the SQLCipher bundle are mutually exclusive — having both
  causes native-provider conflicts. Keep `SQLitePCLRaw.bundle_e_sqlcipher` (or
  whatever the meta-package pulls) as the single provider.

### Opening the DB with a key

- `LocalDatabase` opens the connection with a passphrase via
  `SQLiteConnectionString(dbPath, storeDateTimeAsTicks: true, key: passphrase)`
  (sqlite-net-sqlcipher API). The whole file is encrypted by SQLCipher.

### Key provider abstraction

Add `IDbKeyProvider` with `Task<string> GetKeyAsync()`:

- **MAUI implementation** (`SecureStorageDbKeyProvider`): on first run, generate a
  cryptographically random key, store it in MAUI `SecureStorage` (Android
  Keystore-backed); thereafter read it back. The key never leaves the device.
- **Non-MAUI fallback** (`#if NO_MAUI` / test): return a fixed/test key (or a
  temp-file-backed key) so `SqliteFixture` and mobile unit tests still build and
  run on the `net8.0` target where `SecureStorage` does not exist.

`LocalDatabase` takes `IDbKeyProvider` (or the resolved key) — wired in
`MauiProgram.cs` for the app, and given the fallback in tests.

### Migration of existing deployed devices — wipe + re-sync

SQLCipher cannot open the pre-existing plaintext DB file. Given the app is
offline-first with LWW server sync:

1. On first launch of the encrypted build, detect the legacy plaintext DB
   (e.g. a one-time migration flag in `Preferences`, or attempt-open failure).
2. Delete the old plaintext DB file.
3. Create the new encrypted DB and run a full sync pull from the API to repopulate.

**Accepted tradeoff:** local edits not yet synced to the server at upgrade time are
lost. This is acceptable for offline-first LWW and avoids carrying a plaintext
migration path. (Alternative, if ever needed: `sqlcipher_export` from the old file
into the encrypted DB — not in scope.)

## Data Flow

App code uses `LocalDatabase.Connection` exactly as today; SQLCipher transparently
encrypts/decrypts pages. No repository or sync code changes beyond DB construction
and the one-time migration step.

## Error Handling

- `SecureStorage` read/write failure → surface a clear error; do not silently fall
  back to an unencrypted DB.
- Key must be stable across launches; losing it makes the DB unreadable → wipe +
  re-sync (same path as migration). Document this.
- Migration runs once and is idempotent (guarded by the flag).

## Testing

- Mobile unit tests (`ChildDev.Mobile.Tests`) build and pass on the `net8.0`
  target using the non-MAUI key fallback. Run with:
  `MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true`
- Repository round-trip tests (Goal/Todo/Journal/GoalProgress) pass against an
  encrypted (keyed) connection — proves data is readable with the key.
- Sanity: a DB opened with the wrong/empty key fails to read (confirms encryption
  is actually applied).
- Manual: build the Android APK, verify app launches, migration wipes legacy DB,
  re-sync repopulates, and subsequent launches reuse the SecureStorage key.

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| `bundle_green` + SQLCipher provider conflict | Remove `bundle_green`; single provider |
| `SecureStorage` absent on net8.0 test target | `IDbKeyProvider` non-MAUI fallback |
| Old plaintext DB can't be opened | Wipe + re-sync on first encrypted launch |
| Lost/rotated key bricks local DB | Treat as wipe + re-sync; key stored in Keystore |
| Unsynced local edits lost at upgrade | Accepted for offline-first LWW; documented |
