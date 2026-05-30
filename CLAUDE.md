# ChildDev — Project Context for Claude

## App Purpose
ChildDev helps **kids set and achieve goals**. Tracking (journals, todos, progress notes) exists solely to help kids and their caregivers see where they are in the goal achievement process. The goal entity is the central concept; everything else supports it.

## Core Scope
- **Goals** — the primary entity. Kids (or their caregivers) set developmental goals.
- **GoalProgress** — progress notes and next steps per goal. Helps show advancement toward the goal.
- **Journal** — free-form observations that support goal context.
- **Todos** — tasks that help achieve goals.

When making improvements, always center the experience on goal visibility and progress. Features that don't serve goal achievement are out of scope.

## Tech Stack
- **API:** ASP.NET Core 8 minimal API, EF Core, MariaDB (Pomelo)
- **Mobile:** .NET MAUI (net8.0-android + net8.0), offline-first LWW sync
- **Web UI:** Blazor Server + MudBlazor (Material Design), part of ChildDev.Api project
- **Tests:** xUnit — ChildDev.Api.Tests (API integration), ChildDev.Mobile.Tests (mobile unit)

## Web UI Guidelines
- Use **MudBlazor** components exclusively for the web UI. Do not mix Razor Pages or raw HTML Bootstrap with Blazor components.
- Blazor Server (interactive server-side rendering) — not Blazor WASM.
- Session-based auth for web: store AccountGuid in HttpContext.Session.
- Auth style for web: NickName + PIN (BCrypt — same as mobile registration flow).

## Sync Architecture
- LWW (Last-Write-Wins) by `UpdatedOn` Unix ms timestamp
- Soft deletes: `DeletedAt` field; `UpdatedOn == DeletedAt` on deletion
- `GetModifiedSinceAsync` uses strict `>` (not `>=`)
- Mobile build: `MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true`

## Hard Constraints
- No DB migration files — `EnsureCreated()` manages schema
- No secrets/env edits, no auth logic changes (API JWT), no payment code
- No force-push, no destructive DB ops
- Analytics required in all web UI pages (see global CLAUDE.md)

## Encryption at Rest
- Web: sensitive free-text columns (Goal.GoalText/MeasurableOutcome/Steps, Journal.Notes, GoalProgress.NextStepItems, Todo.Notes) are AES-GCM encrypted via an EF value converter (version tag `v1:`; legacy plaintext reads transparently). Tenant isolation is enforced by EF global query filters keyed to the current account (JWT claim, then web session).
- Key: base64 32-byte `CHILDDEV_ENC_KEY`, sourced from `~/data/.secrets/levelUp.enckey` (gitignored, identical on dev + prod). The API **fails fast** at startup without it. Deploy must export it before `docker compose up`, e.g. `export CHILDDEV_ENC_KEY="$(cat ~/data/.secrets/levelUp.enckey)"`.
- Phase 2 (bounded columns like EmotionReason/Title) needs a one-time `ALTER TABLE … MODIFY … LONGTEXT` (see the post-`EnsureCreated` raw-SQL block in `Program.cs`) before adding them to the converter — `EnsureCreated()` will not widen existing columns.
- Mobile: local SQLite is fully encrypted with SQLCipher; per-device key in MAUI `SecureStorage`.
