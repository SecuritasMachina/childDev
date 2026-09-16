# LevelUp (code name ChildDev) — Project Context for Claude

LevelUp helps **kids set and achieve goals**. **Goal** is the central entity; **GoalProgress** (progress notes, next steps), **Journal** (observations) and **Todos** (tasks) exist only to show kids and caregivers where they are on the way to a goal. Center every change on goal visibility and progress; features that don't serve goal achievement are out of scope.

User-facing name: **LevelUp** (web title, Android app `levelup.securitasmachina.org`, https://levelup.securitasmachina.org). Code, `CHILDDEV_*` env vars, containers and the EDCS appId `childdev` keep **ChildDev**.

## Layout
- `ChildDev.Api/`: ASP.NET Core 8 minimal API + Blazor Server/MudBlazor 7 web UI; EF Core 8 + Pomelo → MariaDB.
- `ChildDev.Api.Tests/` (net8.0, EF in-memory, config injected: no DB or env needed); `ChildDev.Mobile.Tests/` (net9.0).
- `ChildDev.Mobile/LevelUp.csproj`: .NET MAUI 9 (`net9.0-android36.0`; `/p:SkipMauiTargets=true` builds plain `net9.0` with no XAML), offline-first on SQLCipher.
- `playwright/`: web E2E tests. `childDev/`: legacy Ionic/Cordova gitlink, not built. `IMPROVEMENTS.md`: May 2026 loop history, not a backlog (backlog = Gitea issues, `jaxtrx/levelUp`).

## Commands
Use `/usr/bin/dotnet`: the `/usr/local/bin/dotnet` wrapper runs sudo, which drops inline env such as `MSBuildEnableWorkloadResolver=false`.
```bash
/usr/bin/dotnet test ChildDev.Api.Tests/ChildDev.Api.Tests.csproj
MSBuildEnableWorkloadResolver=false /usr/bin/dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true
# Web + API: also export CHILDDEV_JWT_SECRET and CHILDDEV_DB_CONNECTION (a reachable MariaDB)
CHILDDEV_ENC_KEY="$(cat ~/data/.secrets/levelUp.enckey)" /usr/bin/dotnet run --project ChildDev.Api   # http://localhost:5258
```
- XamlC errors surface only in a real `net9.0-android36.0` build, never in the `SkipMauiTargets` test build.
- Signed Play release (AAB + APK): `docker build -f Dockerfile.mobile-release -t levelup-mobile-release:latest .` (source is COPY'd: rebuild after code changes), then `docker run --rm -v ~/data/signingKeys/levelup-release.keystore:/keystore/levelup-release.keystore:ro -v <out>:/out --env-file ~/data/.secrets/levelup-signing.env levelup-mobile-release:latest`. Mount the keystore FILE: `~/data/keystore/` now holds absolute symlinks into `~/data/signingKeys/`, which dangle inside the container ("keystore not mounted"). Version codes burn on upload: check Play tracks before bumping `ApplicationVersion`.
- Prod deploy: `scripts/deploy2web.sh` (Hostwinds, compose project `childdev` in `/opt/childdev`). It gates on both test suites, rsyncs source, copies `~/data/.secrets/childdev-prod.env` over `/opt/childdev/.env`, then hot-deploys (`FORCE_REBUILD=1` recreates the container). Root `deploy.sh` and `scripts/deploy_prod.sh` are older, ungated variants.
- Known broken (Gitea #14): `scripts/build-apk.sh` still targets `net8.0-android` (use `SKIP_APK_BUILD=1`); `scripts/deploy-dev.sh` and the Playwright `baseURL` point at pre-rename targets, so no local dev/E2E instance exists.

## Invariants
- Schema: no EF migration files. `EnsureCreated()` never alters existing tables; add columns with idempotent `ALTER TABLE … ADD COLUMN IF NOT EXISTS` in the raw-SQL block after `EnsureCreated()` in `ChildDev.Api/Program.cs`.
- Sync (Journal, Goal, GoalProgress, Todo): Last-Write-Wins on `UpdatedOn` (Unix ms); soft delete sets `DeletedAt` and `UpdatedOn` to the same instant; `GetModifiedSinceAsync` and the sync endpoints use strict `>` (not `>=`).
- Web UI: Blazor Server interactive rendering (not WASM), MudBlazor components only; no Razor Pages or raw Bootstrap markup.
- Web auth: NickName + PIN, BCrypt as in mobile registration (UI labels it "Password"); the session holds `AccountGuid`. Interactive components can't write the session, so login/register issue a 2-minute one-time token exchanged at `/api/web/auth/complete`. API/mobile auth: JWT bearer (`CHILDDEV_JWT_SECRET`, fail-fast).
- Analytics: pages call `WebAnalyticsService.TrackAsync` (writes `AnalyticsEvents`, forwards to BizEyes). Forward only event name + page, never free-text `context` (children's data). Web BizEyes key: EDCS appId `childdev`, key `analytics.bizeyes.apikey`, soft dependency. Mobile key: gitignored `ChildDev.Mobile/Services/BizEyesConfig.Secret.cs`, empty on a clean checkout by design; never hardcode it.
- Mobile identity (server Guid, JWT, PIN hash, `LastSyncAt`) lives only in the local SQLite `Account` row: never wipe or recreate the local DB without carrying it over.
- Mobile startup: never touch `SecureStorage` synchronously (e.g. in `MauiProgram.CreateMauiApp`); on .NET 9 it deadlocked the splash screen (a Play rejection). Key fetch and DB open stay in `LocalDatabase.InitAsync`.

## Hard Constraints
- No secrets/env edits, no auth logic changes (API JWT), no payment code. **Pending operator review (Gitea #12):** carried over from the May 2026 autonomous loop; conflicts with the global forgot-password/magic-link, spam-protection and fix-high/critical rules.

## Encryption at Rest
- Web: sensitive free-text columns (Goal.GoalText/MeasurableOutcome/Steps, Journal.Notes, GoalProgress.NextStepItems, Todo.Notes) are AES-GCM encrypted via an EF value converter (`Data/EncryptedStringConverter.cs`, version tag `v1:`; legacy plaintext reads transparently). `EncryptionMigrationHostedService` re-encrypts leftover plaintext rows at startup (idempotent). Tenant isolation is enforced by EF global query filters keyed to the current account (JWT claim, then web session); a null account sees zero rows; `Account` is unfiltered (login by NickName).
- Key: base64 32-byte `CHILDDEV_ENC_KEY`, sourced from `~/data/.secrets/levelUp.enckey` (identical on dev + prod). The API **fails fast** at startup without it (on prod: crash loop, Traefik 404). Compose reads it from `/opt/childdev/.env`, which `deploy2web.sh` overwrites with `childdev-prod.env`, so the key must live in that file (Gitea #13). A manual `docker compose up` elsewhere needs `export CHILDDEV_ENC_KEY="$(cat ~/data/.secrets/levelUp.enckey)"` first.
- Phase 2 (bounded columns like EmotionReason/Title) needs a one-time `ALTER TABLE … MODIFY … LONGTEXT` (in the post-`EnsureCreated` raw-SQL block in `Program.cs`) before adding them to the converter — `EnsureCreated()` will not widen existing columns.
- Mobile: local SQLite is fully encrypted with SQLCipher; per-device key in MAUI `SecureStorage`.
