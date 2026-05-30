# Web Tier — Encryption at Rest & Tenant Isolation (Design)

**Date:** 2026-05-29
**Project:** ChildDev.Api (Blazor Server + mobile-sync API, MariaDB)
**Scope:** Web/API tier only. Mobile tier is covered by a separate spec
(`2026-05-29-mobile-encryption-at-rest-design.md`).

## Goals

1. **Encryption at rest** — sensitive free-text content stored in MariaDB is
   encrypted so a database/volume dump does not expose user content in plaintext.
2. **Tenant isolation (defense-in-depth)** — web clients cannot see each other's
   data even if a developer forgets a per-query `.Where(AccountFk == ...)` filter.

## Non-Goals

- No MariaDB server-side / transparent tablespace encryption (would require
  `my.cnf` + keyfile edits = infra/secrets changes, against project constraints).
- No changes to authentication logic (JWT issuance/validation, PIN/BCrypt,
  session establishment). We only *read* the already-authenticated identity.
- No DB migration files. `EnsureCreated()` remains the schema manager.

## Current State (verified)

- `AppDbContext` is **shared** by two callers:
  - **Blazor pages** via `IDbContextFactory<AppDbContext>`; current account read
    from `HttpContext.Session["AccountGuid"]`.
  - **Mobile-sync API endpoints** via scoped `AppDbContext`; current account read
    from the JWT claim through `jwt.ExtractAccountGuid(user)`.
- Both `AddDbContext<AppDbContext>` and `AddDbContextFactory<AppDbContext>` are
  registered in `Program.cs`.
- Isolation today is **manual per-query** filtering on every Razor page and API
  endpoint. The API sync path additionally rejects cross-account records.
- Tenant key column is `AccountFk` (string, 36) on Goal/Journal/GoalProgress/Todo,
  and `AccountGuid` (string, 36) on Reminder/AnalyticsEvent. These stay plaintext.

## Part 1 — Tenant Isolation via EF Global Query Filters

### Current-account provider

Add `ICurrentAccountProvider` with a single `string? GetAccountGuid()`:

1. If the request has an authenticated JWT principal, return
   `jwt.ExtractAccountGuid(user)` (mobile-sync / API path).
2. Else fall back to `HttpContext.Session.GetString("AccountGuid")` (Blazor).
3. Else return `null`.

Reading the authenticated identity is **not** an auth-logic change — issuance and
validation are untouched. Registered scoped, backed by `IHttpContextAccessor`.

### Applying the filter

- Add a nullable `string? AccountGuid` property to `AppDbContext`.
- In `OnModelCreating`, add `HasQueryFilter` to the **tenant entities only**:
  - `Goal`, `Journal`, `GoalProgress`, `Todo` → `e => e.AccountFk == AccountGuid`
  - `Reminder`, `AnalyticsEvent` → `e => e.AccountGuid == AccountGuid`
- **Do NOT filter `Account`.** Login/register look up Account by `NickName`
  before any AccountGuid exists; filtering it breaks auth.
- When `AccountGuid` is null (pre-login), tenant entities return zero rows — the
  safe default.

### Wiring so it "can't be forgotten"

- **API (scoped `AppDbContext`):** set `db.AccountGuid` from
  `ICurrentAccountProvider` at context creation. Simplest: do it in a tiny
  `AppDbContext` constructor hook or an interceptor; acceptable fallback is
  setting it in a `SaveChanges`-independent scoped initializer.
- **Blazor (`IDbContextFactory`):** the factory creates contexts outside request
  scope, so wrap it. Introduce `ScopedDbContextFactory : IDbContextFactory<AppDbContext>`
  that delegates to the real factory and sets `AccountGuid` from the provider on
  every `CreateDbContext()`/`CreateDbContextAsync()`. Register the wrapper so all
  existing `@inject IDbContextFactory<AppDbContext>` pages get scoping for free.

### Interaction with existing manual filters

Existing `.Where(x => x.AccountFk == account)` clauses become redundant but
harmless (filter is idempotent). Leave them; they document intent and protect any
code path that bypasses the provider. The API mobile-sync mismatch-rejection logic
stays as-is (it guards writes, which query filters do not cover).

## Part 2 — Encryption at Rest via AES-GCM Value Converter

### Algorithm & format

- AES-256-GCM. Per-value random 12-byte nonce. Stored string format:
  `v1:` + base64( nonce(12) ‖ tag(16) ‖ ciphertext ).
- **Read path is version-tagged & backward compatible:** if the stored value does
  not start with `v1:`, it is legacy plaintext → return as-is. This makes the
  converter idempotent and enables zero-downtime lazy migration on a live DB.
- Null/empty values pass through unchanged.

Implemented as an EF Core `ValueConverter<string, string>` applied per column via
Fluent API.

### Key management

- 32-byte AES key, base64-encoded, **source of truth is the keyfile**
  `~/data/.secrets/levelUp.enckey` (already provisioned with an identical key on
  both the local/dev host and the remote prod host `hwsrv-...hostwindsdns.com`,
  perms `600`). Using the same key on both hosts keeps encrypted data portable
  between dev and prod.
- The app reads the key via env var `CHILDDEV_ENC_KEY`. The deployment supplies it
  from the keyfile — either by mounting the file into the container and reading its
  contents, or by exporting `CHILDDEV_ENC_KEY="$(cat ~/data/.secrets/levelUp.enckey)"`
  into the compose env. Startup **fails fast** if the key is missing or not 32
  bytes after base64-decode.
- `CHILDDEV_ENC_KEY` is documented in `.env.example` (placeholder only). The real
  key lives solely in the gitignored keyfile — never committed.

### Columns to encrypt — Phase 1 (no schema change)

`EnsureCreated()` never **alters** existing columns, so on the live DB we may only
encrypt columns already mapped to `TEXT`/`LONGTEXT` — i.e. the **unbounded
`string?`** fields. Ciphertext fits these without any `ALTER`:

- `Goal.GoalText`, `Goal.MeasurableOutcome`, `Goal.Steps`
- `Journal.Notes`
- `GoalProgress.NextStepItems`
- `Todo.Notes`

These hold the bulk of the actual user-written content.

### Columns deferred — Phase 2 (opt-in, requires manual one-time ALTER)

These are sensitive but currently `VARCHAR(n)`; ciphertext (~+50% + base64) would
overflow, and `EnsureCreated()` will not widen them on the live DB:

- `Journal.EmotionReason` (1000), `Journal.Activity` (255), `Journal.Tags` (500)
- `Todo.Title` (500)
- `Reminder.Title` (200), `Reminder.EntityLabel` (200)

To encrypt these later, run a one-time manual `ALTER TABLE ... MODIFY ... TEXT`
(an explicit, documented deviation from the EnsureCreated guarantee), then move
the column into the encrypted set. Left out of Phase 1 to honor the no-migration
constraint. `AnalyticsEvent` is telemetry (categorical, low sensitivity) — not
encrypted.

### Lazy migration of existing rows

- New writes are encrypted immediately by the converter.
- A one-shot, idempotent **background re-save pass** runs after startup
  (`IHostedService`): for each Phase-1 entity, read rows whose target column lacks
  the `v1:` prefix in small batches and re-save them (the converter encrypts on
  write). Uses `IgnoreQueryFilters()` so it spans all tenants. Safe to re-run;
  skips already-encrypted rows. No downtime.

## Data Flow Summary

Write: entity property (plaintext in memory) → ValueConverter encrypts → `v1:...`
stored in MariaDB. Read: `v1:...` → ValueConverter decrypts → plaintext in memory;
legacy plaintext passes through. All reads of tenant entities are auto-scoped to
the current account by the global query filter.

## Error Handling

- Missing/invalid `CHILDDEV_ENC_KEY` at startup → app fails fast with a clear log
  (no key material logged).
- Decryption failure on a `v1:` value → throw (indicates key mismatch / corruption;
  must not silently return garbage). Legacy plaintext (no prefix) never reaches
  the decryptor.
- Null current account → empty tenant result sets, never another tenant's data.

## Testing

- **Converter unit tests:** round-trip encrypt→decrypt; legacy plaintext pass-through;
  null/empty pass-through; idempotency (`v1:` not double-wrapped); tamper → throw.
- **Query-filter tests (API integration, `ChildDev.Api.Tests`):**
  - Two accounts; account A's queries never return account B's Goal/Journal/
    GoalProgress/Todo/Reminder/AnalyticsEvent.
  - Mobile-sync endpoints (JWT path) still return the caller's own rows — guards
    against the shared-context regression where session-only scoping would zero
    out API results.
  - `Account` lookup by NickName (login) still works (not filtered).
- **Migration test:** seed legacy plaintext row → run re-save pass → row is `v1:`
  and still decrypts to original.
- Run full `ChildDev.Api.Tests` suite; confirm existing auth/sync tests pass.

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Shared context: session-only scoping breaks mobile sync | Provider resolves JWT first, then session; explicit API-path test |
| Filtering `Account` breaks login | `Account` excluded from filters |
| Live plaintext rows unreadable after converter goes live | Version-tagged converter treats no-prefix as plaintext |
| Ciphertext overflows bounded columns | Phase 1 limited to unbounded TEXT columns |
| Key in source control | New gitignored env var; `.env.example` doc only; fail-fast |
| Lost ability to search encrypted columns in SQL | Accepted; these are free-text content, not query keys |
