# ChildDev — MAUI Migration Design

**Date:** 2026-05-16
**Status:** Approved

## Summary

Migrate the ChildDev childhood-development/thought-organizer app from Ionic/Angular + Java backend to .NET MAUI (mobile) + ASP.NET Core Razor API + MariaDB (server). The MAUI app is fully standalone — SQLite on device is always the source of truth. The server is an optional sync target for multi-device support, not a gatekeeper.

---

## Goals

- Run fully offline on iOS and Android (no network required for core use)
- Multi-device sync via a C# Razor REST API + MariaDB hosted in Docker
- Replace all Java backend endpoints with C# equivalents
- Replace Ionic/Angular frontend with .NET MAUI
- Add a Dashboard home screen summarizing recent activity
- Follow existing Docker/Traefik patterns from the host environment

## Non-Goals

- No web UI (the Razor project is API-only)
- No real-time/push sync (SignalR deferred)
- No social/sharing features
- No existing Ionic/Angular code preserved — clean rewrite

---

## Architecture

```
┌─────────────────────────────────────────────────────┐
│                   MAUI Mobile App                    │
│                                                      │
│  ┌──────────┐  ┌──────────┐  ┌──────────────────┐  │
│  │ Journal  │  │  Goals   │  │      Todos        │  │
│  └──────────┘  └──────────┘  └──────────────────┘  │
│                                                      │
│  ┌──────────────────────────────────────────────┐   │
│  │         Local SQLite (always source of truth) │   │
│  └──────────────────────────────────────────────┘   │
│                                                      │
│  ┌──────────────────────────────────────────────┐   │
│  │    SyncService — fires on app open + online   │   │
│  └──────────────────────┬─────────────────────┘    │
└───────────────────────────┼─────────────────────────┘
                            │ HTTPS REST (JWT)
                            ▼
┌─────────────────────────────────────────────────────┐
│           ASP.NET Core Minimal API                  │
│        (docker-compose-beta.yml, Traefik)           │
│                                                      │
│  ┌──────────────────────────────────────────────┐   │
│  │              MariaDB 11                       │   │
│  └──────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────┘
```

**Key invariants:**
- SQLite on device is always authoritative; sync failure is silent and retried next app open
- All records carry `Guid`, `AccountFk`, `UpdatedOn` (Unix ms), `DeletedAt` (soft delete)
- `UpdatedOn` higher timestamp wins on conflict (last-write-wins)
- Server account is created lazily on first successful sync

---

## MAUI App (`ChildDev.Mobile/`)

### Project Structure

```
ChildDev.Mobile/
├── Models/
│   ├── SyncBase.cs            # Guid, AccountFk, UpdatedOn, DeletedAt
│   ├── Journal.cs
│   ├── Goal.cs
│   ├── GoalProgress.cs
│   ├── Todo.cs
│   └── Account.cs             # Local account: NickName, PinHash, LastSyncAt
├── Data/
│   ├── LocalDatabase.cs       # SQLite-net-pcl connection/init
│   ├── JournalRepository.cs
│   ├── GoalRepository.cs
│   ├── GoalProgressRepository.cs
│   └── TodoRepository.cs
├── Services/
│   ├── AccountService.cs      # Local account creation, PIN verify, JWT cache
│   ├── SyncService.cs         # On-open sync orchestrator
│   └── ConnectivityService.cs # Wraps IConnectivity, mockable for tests
├── ViewModels/
│   ├── DashboardViewModel.cs
│   ├── JournalListViewModel.cs
│   ├── JournalEntryViewModel.cs
│   ├── GoalListViewModel.cs
│   ├── GoalEntryViewModel.cs
│   ├── TodoListViewModel.cs
│   └── SettingsViewModel.cs
├── Views/
│   ├── DashboardPage.xaml     # Recent activity summary, quick-add buttons
│   ├── JournalListPage.xaml
│   ├── JournalEntryPage.xaml
│   ├── GoalListPage.xaml
│   ├── GoalEntryPage.xaml
│   ├── TodoListPage.xaml
│   ├── SettingsPage.xaml      # Server URL, sync status, account info
│   └── SetupPage.xaml         # First-launch: nickname + 4-digit PIN
├── AppShell.xaml              # Bottom tab nav: Dashboard | Journal | Goals | Todos
└── MauiProgram.cs             # DI registration
```

### Local Account Bootstrap

1. On first launch, no `Account` record in SQLite → navigate to `SetupPage`
2. User enters nickname + 4-digit PIN
3. PIN hashed with BCrypt, stored in `Account` table
4. Shell loads — app is fully functional immediately
5. Server registration happens lazily on first sync attempt

### Navigation

Shell with four bottom tabs: **Dashboard | Journal | Goals | Todos**. Detail pages (JournalEntry, GoalEntry) push onto the navigation stack. No modal dialogs for data entry.

### Data Model

```csharp
// Base for all synced entities
public record SyncBase {
    [PrimaryKey] public string Guid { get; init; } = System.Guid.NewGuid().ToString();
    public string AccountFk { get; init; } = string.Empty;
    public long UpdatedOn { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long? DeletedAt { get; set; }
}

public record Journal : SyncBase {
    public string? Notes { get; set; }
    public string? Activity { get; set; }
    public string? Mood { get; set; }
    public string? Tags { get; set; }
    public long EnteredDate { get; set; }
}

public record Goal : SyncBase {
    public string? GoalText { get; set; }
    public long? NextMeetingDate { get; set; }
    public long? ExpirationDate { get; set; }
    public long EnteredDate { get; set; }
    public string? MeasurableOutcome { get; set; }
    public long? CompletionDate { get; set; }
}

public record GoalProgress : SyncBase {
    public string GoalFk { get; set; } = string.Empty;
    public string? NextStepItems { get; set; }
    public long? NextMeetingDate { get; set; }
}

public record Todo : SyncBase {
    public string? Title { get; set; }
    public string? Notes { get; set; }
    public long? DueDate { get; set; }
    public long? CompletedAt { get; set; }
}

public record Account {
    [PrimaryKey] public string Guid { get; init; } = System.Guid.NewGuid().ToString();
    public string NickName { get; set; } = string.Empty;
    public string PinHash { get; set; } = string.Empty;
    public long CreatedOn { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long LastSyncAt { get; set; } = 0;
    public string? ServerJwt { get; set; }
    public string? ServerUrl { get; set; }
}
```

### Dependencies

- `sqlite-net-pcl` — local SQLite ORM
- `CommunityToolkit.Mvvm` — MVVM source generators
- `BCrypt.Net-Next` — PIN hashing
- `Microsoft.Extensions.Http` — typed HttpClient for sync

---

## API (`ChildDev.Api/`)

### Project Structure

```
ChildDev.Api/
├── Endpoints/
│   ├── AuthEndpoints.cs
│   ├── JournalEndpoints.cs
│   ├── GoalEndpoints.cs
│   ├── GoalProgressEndpoints.cs
│   └── TodoEndpoints.cs
├── Models/
│   ├── Entities/              # EF Core entity classes
│   └── Dtos/                  # Request/response DTOs
├── Data/
│   └── AppDbContext.cs        # Pomelo MariaDB EF Core context
├── Services/
│   └── SyncService.cs         # Batch upsert: higher UpdatedOn wins
├── sql/
│   └── init.sql               # MariaDB schema, run on first container start
└── Dockerfile
```

### Endpoints

```
POST /api/auth/register          { nickName, pinHash }  → { jwt, accountGuid }
POST /api/auth/token             { nickName, pinHash }  → { jwt, accountGuid }

POST /api/sync/journal           [JournalDto]           → [JournalDto]  (upsert + return delta)
POST /api/sync/goal              [GoalDto]              → [GoalDto]
POST /api/sync/goal-progress     [GoalProgressDto]      → [GoalProgressDto]
POST /api/sync/todo              [TodoDto]              → [TodoDto]
```

All `/api/sync/*` endpoints require `Authorization: Bearer <jwt>` header.

### Sync Protocol (per entity, per app open)

1. MAUI sends all local records with `UpdatedOn > lastSyncAt`
2. Server upserts — if server record has higher `UpdatedOn`, server version wins; otherwise client version wins
3. Server returns all records for the account with `UpdatedOn > lastSyncAt`
4. MAUI upserts server records into SQLite using same rule
5. MAUI sets `Account.LastSyncAt = now` only on HTTP 200

### Authentication

- PIN is hashed on device with BCrypt before transmission — server never sees raw PIN
- JWT issued by server, stored in `Account.ServerJwt` in local SQLite
- JWT expiry: 90 days; re-authenticate silently using stored PIN hash

### Error Handling

- Any sync failure (network, 4xx, 5xx) is swallowed silently — app continues normally
- `SettingsPage` shows last sync timestamp and last error message for user visibility
- No retry backoff in v1 — next app open retries

---

## MariaDB Schema

```sql
CREATE TABLE Account (
    Guid        CHAR(36) PRIMARY KEY,
    NickName    VARCHAR(100) NOT NULL,
    PinHash     VARCHAR(100) NOT NULL,
    CreatedOn   BIGINT NOT NULL,
    INDEX idx_account_nickname (NickName)
);

CREATE TABLE Journal (
    Guid        CHAR(36) PRIMARY KEY,
    AccountFk   CHAR(36) NOT NULL,
    Notes       TEXT,
    Activity    VARCHAR(255),
    Mood        VARCHAR(50),
    Tags        VARCHAR(500),
    EnteredDate BIGINT NOT NULL,
    UpdatedOn   BIGINT NOT NULL,
    DeletedAt   BIGINT,
    INDEX idx_journal_account_updated (AccountFk, UpdatedOn)
);

CREATE TABLE Goal (
    Guid               CHAR(36) PRIMARY KEY,
    AccountFk          CHAR(36) NOT NULL,
    GoalText           TEXT,
    NextMeetingDate    BIGINT,
    ExpirationDate     BIGINT,
    EnteredDate        BIGINT NOT NULL,
    MeasurableOutcome  TEXT,
    CompletionDate     BIGINT,
    UpdatedOn          BIGINT NOT NULL,
    DeletedAt          BIGINT,
    INDEX idx_goal_account_updated (AccountFk, UpdatedOn)
);

CREATE TABLE GoalProgress (
    Guid             CHAR(36) PRIMARY KEY,
    AccountFk        CHAR(36) NOT NULL,
    GoalFk           CHAR(36) NOT NULL,
    NextStepItems    TEXT,
    NextMeetingDate  BIGINT,
    UpdatedOn        BIGINT NOT NULL,
    DeletedAt        BIGINT,
    INDEX idx_goalprogress_account_updated (AccountFk, UpdatedOn)
);

CREATE TABLE Todo (
    Guid        CHAR(36) PRIMARY KEY,
    AccountFk   CHAR(36) NOT NULL,
    Title       VARCHAR(500),
    Notes       TEXT,
    DueDate     BIGINT,
    CompletedAt BIGINT,
    UpdatedOn   BIGINT NOT NULL,
    DeletedAt   BIGINT,
    INDEX idx_todo_account_updated (AccountFk, UpdatedOn)
);
```

---

## Docker (`/mnt/8TB_HDD_DATA/shared/src/docker/docker-compose-beta.yml`)

```yaml
services:
  childdev-api:
    build:
      context: ../childDev/ChildDev.Api
      dockerfile: Dockerfile
    container_name: childdev-api
    env_file:
      - /home/jaxtrx/data/.secrets/childdev-beta.env
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - CHILDDEV_HTTP_PORT=8080
    networks:
      - beta_default
    restart: unless-stopped
    depends_on:
      - childdev-db

  childdev-db:
    image: mariadb:11
    container_name: childdev-db
    env_file:
      - /home/jaxtrx/data/.secrets/childdev-beta.env
    volumes:
      - childdev_db_data:/var/lib/mysql
      - ../childDev/ChildDev.Api/sql/init.sql:/docker-entrypoint-initdb.d/init.sql:ro
    networks:
      - beta_default
    restart: unless-stopped

networks:
  beta_default:
    driver: bridge

volumes:
  childdev_db_data:
```

**Secrets file** (`/home/jaxtrx/data/.secrets/childdev-beta.env`) — not committed:
```
MARIADB_ROOT_PASSWORD=<secret>
MARIADB_DATABASE=childdev
MARIADB_USER=childdev
MARIADB_PASSWORD=<secret>
CHILDDEV_DB_CONNECTION=Server=childdev-db;Database=childdev;User=childdev;Password=<secret>
CHILDDEV_JWT_SECRET=<at-least-32-chars>
```

---

## Migration from Ionic/Angular

The existing Ionic/Angular code in `childDev/childDev/` is **not carried forward** — this is a clean rewrite. The existing codebase documents the feature set and data shapes; the new projects live in:

```
childDev/
├── ChildDev.Mobile/      # .NET MAUI app (new)
├── ChildDev.Api/         # ASP.NET Core API (new)
├── childDev/             # Ionic/Angular (reference only, to be archived)
└── docs/
    └── superpowers/specs/
        └── 2026-05-16-childdev-maui-migration-design.md
```

---

## Testing Strategy

- `ChildDev.Api.Tests/` — xUnit, in-memory SQLite via EF Core for endpoint tests
- `ChildDev.Mobile.Tests/` — xUnit, mock `IConnectivity` and `ISyncService` for ViewModel tests
- Sync protocol tested with a fake server that returns known deltas
- No UI automation in v1

---

## Out of Scope (v1)

- Traefik TLS/routing config for childdev-api (can be added to `traefik/apps.dynamic.yml` later)
- Push notifications
- Export/import to file
- Web UI
- Apple App Store / Google Play submission
