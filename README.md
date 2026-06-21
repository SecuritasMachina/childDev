# LevelUp (ChildDev)

LevelUp is a goal-achievement app for kids. It helps children — and their caregivers — **set developmental goals and see their progress toward achieving them**. Goals are the central concept; everything else (journals, todos, progress notes) exists to make goal progress visible.

The solution ships as two front ends sharing one backend:

- A **.NET MAUI mobile app** (Android), offline-first with local encrypted storage and background sync.
- A **Blazor Server web UI** (MudBlazor / Material Design), served from the same ASP.NET Core project that hosts the API.

> The repository directory is named `levelUp`; the product/display name is **LevelUp**; the internal solution and namespaces use **ChildDev**.

## Overview

The goal entity is the primary object. Each goal can carry measurable outcomes, steps, and a stream of progress notes. Supporting entities — free-form journals and todos — provide context and tasks that help advance a goal. The experience is intentionally centered on goal visibility and progress; features that don't serve goal achievement are out of scope.

The mobile app works fully offline and syncs with the API using a Last-Write-Wins reconciliation model. Sensitive free-text content is encrypted at rest on both tiers.

## Key Features

- **Goals** — the primary entity; kids or caregivers set developmental goals with measurable outcomes and steps.
- **Goal progress** — progress notes and next-step items per goal, showing advancement over time.
- **Journals** — free-form observations that add context to goals.
- **Todos** — tasks that help achieve goals.
- **Offline-first mobile** — full local functionality with no connectivity, syncing when online.
- **Last-Write-Wins sync** — conflict resolution by `UpdatedOn` (Unix ms) timestamp, with soft deletes.
- **Encryption at rest** — AES-GCM encryption of sensitive columns on the server; fully encrypted local SQLite (SQLCipher) on mobile, with the per-device key in platform secure storage.
- **Tenant/account isolation** — enforced via EF Core global query filters keyed to the current account.
- **Local notifications** — reminders on mobile via local notification plugin.
- **Web + mobile auth** — NickName + PIN registration (BCrypt), JWT for the API, session-based auth for the web UI.

## Tech Stack

| Area | Technology |
|------|-----------|
| API | ASP.NET Core 8 (minimal API), EF Core 8, Pomelo MySQL provider, MariaDB |
| Web UI | Blazor Server (interactive server-side rendering) + MudBlazor 7 |
| Mobile | .NET MAUI (net8.0-android), CommunityToolkit.Mvvm, sqlite-net-sqlcipher |
| Auth | JWT bearer (API), session (web), BCrypt password/PIN hashing |
| Encryption | AES-GCM column encryption (server), SQLCipher (mobile) |
| Config/Analytics | EDCS AppConfig client (soft dependency, supplies the web analytics key) |
| API docs | Swashbuckle / OpenAPI (Swagger) |
| Tests | xUnit (API integration + mobile unit) |
| Packaging | Docker, Docker Compose, Traefik (TLS via Let's Encrypt / Cloudflare DNS-01) |

## Architecture

```
ChildDev.sln
├── ChildDev.Api           ASP.NET Core 8 — REST API + Blazor Server web UI
│   ├── Endpoints          Minimal-API route handlers
│   ├── Components / Pages  Blazor Server UI (MudBlazor) + Razor pages
│   ├── Data               EF Core DbContext, query filters, value converters
│   ├── Models             Entities + DTOs
│   └── Services           Domain/sync/encryption services
├── ChildDev.Api.Tests     xUnit integration tests for the API
├── ChildDev.Mobile        .NET MAUI app (LevelUp.csproj) — MVVM, offline SQLite, sync
└── ChildDev.Mobile.Tests  xUnit unit tests for the mobile project
```

### Sync model

- **Last-Write-Wins** by `UpdatedOn` (Unix milliseconds).
- **Soft deletes** via a `DeletedAt` field; on deletion `UpdatedOn == DeletedAt`.
- Incremental pulls use a strict `>` comparison against the last-seen timestamp.

### Schema management

- Schema is created with EF Core `EnsureCreated()` — there are **no migration files**.
- Because `EnsureCreated()` does not widen existing columns, columns that need to grow to hold encrypted text are adjusted via a one-time raw-SQL `ALTER TABLE` step at startup.

## Build & Run

### Prerequisites

- .NET 8 SDK (for the API and web UI).
- .NET MAUI workload with the Android workload installed (for the mobile app).
- Docker + Docker Compose (for the containerized stack).
- A reachable MariaDB instance (provided by the compose stack, or your own).

### API + Web UI (local)

```bash
dotnet run --project ChildDev.Api
```

This serves both the REST API and the Blazor Server web UI. Swagger/OpenAPI is available in development.

### Full stack via Docker Compose

```bash
cp .env.example .env      # then fill in the required values
docker compose up --build
```

The compose stack runs the API, MariaDB, a static downloads host, and Traefik (HTTPS termination). See the compose file for service details.

### Mobile (Android)

Use the provided build script, which always does a clean build and stages the APK:

```bash
./scripts/build-apk.sh                  # Release build (default)
CONFIG=Debug ./scripts/build-apk.sh     # Debug build (embeds assemblies for sideload)
```

### Tests

```bash
# API tests
dotnet test ChildDev.Api.Tests/ChildDev.Api.Tests.csproj

# Mobile tests (skip MAUI/Android targets so they run without the Android workload)
MSBuildEnableWorkloadResolver=false \
  dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true
```

## Configuration

Configuration is supplied through environment variables (see `.env.example`). **No real secrets are committed** — placeholder values only.

Required:

- `CHILDDEV_JWT_SECRET` — signing secret for API JWTs.
- `MARIADB_ROOT_PASSWORD` — database root password.
- `CHILDDEV_ENC_KEY` — base64-encoded 32-byte AES key for at-rest column encryption. The API **fails fast at startup** if this is missing or invalid. Generate one with `openssl rand -base64 32`. The real value must be kept outside the repo and identical across environments so encrypted data remains readable.

Optional:

- `CHILDDEV_DB_CONNECTION` / `CHILDDEV_DB_PASSWORD` — database connection details.
- `EDCS_*` — EDCS AppConfig client settings. This is a **soft dependency**: if unset or unreachable, analytics forwarding stays disabled and the app still runs.

### Notes

- The web UI uses MudBlazor components exclusively; it is Blazor Server (not WASM).
- Mobile local storage is encrypted with SQLCipher; the per-device key lives in the platform's secure storage.
- The app displays a build timestamp (Eastern time) in the UI, stamped at build time via assembly metadata.

## License

Proprietary — all rights reserved.
