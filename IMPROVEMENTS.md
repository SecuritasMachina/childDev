# Improvement Log

## 2026-05-16 — Iteration 26 Brainstorm (fresh — every 3rd)

| # | Description | Dim | Impact | Effort | Risk | Status |
|---|-------------|-----|--------|--------|------|--------|
| 1 | DashboardPage: recent journal cards tap to open entry | UI | High | S | Low | **DONE** |

| 2 | JournalEntryPage: Delete button (currently list-only) | Func | Medium | S | Low | **DONE** |
| 3 | GoalListPage: show ExpirationDate per goal | UI | Medium | S | Low | **DONE** |
| 4 | API: response compression (gzip) | Perf | Low | S | Low | **DONE** |
| 5 | TodoList: pinch-to-expand completed todos (toggle) | UI | Low | M | Medium | Backlog |
| 6 | JournalEntryPage: entered date picker (allow backdating) | Func | Medium | M | Low | Backlog |
| 7 | SettingsPage: show account info (nick, created date) | UI | Low | S | Low | **DONE** |
| 8 | GoalEntry: validate GoalText is non-empty before save | Stability | Medium | S | Low | **DONE** |
| 9 | API: request timeout middleware (cancel slow DB queries) | Stability | Low | S | Low | **DONE** |
| 10 | TodoEntry: validate Title non-empty before save | Stability | Medium | S | Low | Backlog |

---

## 2026-05-16 — API structured logging + CORS + X-Request-ID + Sync retry + Dashboard sync time

**Iterations 20-25 summary:**

**Iter 20 — Dashboard last-synced timestamp:**
`DashboardViewModel`: Added `LastSyncDisplay` populated from `account.LastSyncAt` in `LoadAsync`, updated after successful sync. `DashboardPage.xaml`: secondary gray label below SyncStatus.

**Iter 21 — GoalListPage: NextMeetingDate per row:**
Added `MeetingDateConverter` ("Meet: Mon, May 18"). Registered in `App.xaml`. Added label to GoalListPage row.

**Iter 22 — API: X-Request-ID echo header:**
Program.cs middleware echoes client-provided `X-Request-ID` or generates a 12-char hex ID. 2 new tests. 44 API tests.

**Iter 23 — SyncService: retry on 5xx/HttpRequestException:**
`SyncEntityAsync` retries immediately once on first 5xx or network exception. LWW-safe (server ignores duplicate records with same UpdatedOn). Added `TransientFailThenSucceedHandler` test. 22 mobile tests.

**Iter 24 — API: CORS policy:**
`AddCors` with configurable `CHILDDEV_CORS_ORIGIN` env var (default: localhost:4200). `app.UseCors()` before auth middleware.

**Iter 25 — API: structured debug logging:**
All 4 sync endpoints log `account[:8]`, incoming count, delta count at `Debug` level via a named logger created at registration time.

---

## 2026-05-16 — Iteration 19 Brainstorm (fresh — every 3rd)

| # | Description | Dim | Impact | Effort | Risk | Status |
|---|-------------|-----|--------|--------|------|--------|
| 1 | GoalEntryPage: ExpirationDate date picker + display | Func | High | S | Low | **DONE** |
| 2 | Dashboard: show last-synced timestamp (not just "Synced HH:mm") | UI | Medium | S | Low | **DONE** |
| 3 | API: X-Request-ID echo header for traceability | Perf | Low | S | Low | **DONE** |
| 4 | SyncService: retry once on transient HTTP failure | Stability | Low | M | Medium | **DONE** |
| 5 | GoalListPage: show NextMeetingDate per goal | UI | Medium | S | Low | **DONE** |
| 6 | API: CORS policy — restrict to expected origins | Security | Low | S | Low | **DONE** |
| 7 | JournalListPage: activity chip/badge alongside mood | UI | Low | S | Low | Backlog |
| 8 | TodoEntryPage: due date picker UI (DatePicker element) | Func | Medium | S | Low | Backlog |
| 9 | API: structured logging for sync (record counts, account) | Ops | Low | S | Low | **DONE** |
| 10 | GoalEntryPage: clear ExpirationDate (nullable DatePicker) | Func | Low | S | Low | Backlog |

---

## 2026-05-16 — Pull-to-refresh sync on Journal, Goal, and Todo list pages

**What changed:**
- `JournalListViewModel`, `GoalListViewModel`, `TodoListViewModel`: Added `SyncService` constructor parameter, `IsRefreshing` observable, and `RefreshCommand`. Refresh runs sync, reloads data from local DB, clears status message on success, sets error on failure, always sets `IsRefreshing = false` in finally.
- `JournalListPage.xaml`, `GoalListPage.xaml`: Wrapped `CollectionView` in `RefreshView` bound to `RefreshCommand`/`IsRefreshing`.
- `TodoListPage.xaml`: Same RefreshView wrapper around the CollectionView (Grid.Row="2").

**Why:** Users had no way to trigger sync from within a list page. They had to navigate to the dashboard and wait for auto-sync. Pull-to-refresh is the standard mobile pattern for on-demand refresh.

**Impact:** All three list pages now support pull-to-refresh, triggering a full sync + local reload. 21 mobile tests, 42 API tests pass.

---

## 2026-05-16 — Iteration 16 Brainstorm (fresh — every 3rd)

| # | Description | Dim | Impact | Effort | Risk | Status |
|---|-------------|-----|--------|--------|------|--------|
| 1 | JournalListPage: pull-to-refresh trigger sync | Func | Medium | M | Medium | **DONE** |
| 2 | API: X-Request-ID header echo for traceability | Perf | Low | S | Low | Backlog |
| 3 | SyncService: retry once on transient failure | Stability | Low | M | Medium | Backlog |
| 4 | GoalEntryPage: show ExpirationDate label for existing | Func | Low | S | Low | Backlog |
| 5 | TodoRepository: count completed todos (for "X done" footer) | UI | Low | S | Low | **DONE** |
| 6 | API: CORS policy — allow only expected mobile origins | Stability | Low | S | Low | Backlog |
| 7 | JournalListPage: word count in preview (character/word count) | UI | Low | S | Low | Backlog |
| 8 | API: limit Records list size to avoid unbounded POST body | Stability | Medium | S | Low | **DONE** |
| 9 | SyncService: structured logging (last sync time, record counts) | Perf | Low | S | Low | Backlog |
| 10 | TodoEntryViewModel: ExpirationDate / reminder date support | Func | Low | M | Low | Backlog |

---

## 2026-05-16 — Completed todo count footer on TodoListPage

**What changed:**
- `TodoRepository`: Added `GetCompletedCountAsync(accountFk)` — counts todos with non-null `CompletedAt` and null `DeletedAt`.
- `TodoListViewModel`: Added `CompletedTodoCount` and `HasCompletedTodos` observables. `LoadAsync` fetches the count; `CompleteAsync` increments it live. Added `StatusMessage` + try/catch error boundary.
- `TodoListPage.xaml`: Extended Grid to `RowDefinitions="Auto,Auto,*,Auto"`. Row 0 = error label; Row 3 = footer showing "N task(s) completed" (hidden when count = 0).

**Why:** Users completing tasks had no feedback that tasks were being archived rather than disappearing. The footer shows accumulating completions without cluttering the pending list.

**Impact:** Completion progress visible at a glance. 21 mobile tests, 42 API tests pass.

---

## 2026-05-16 — API: reject future UpdatedOn (422) + goal sort + error boundaries

**Iterations 14-17 summary:**

**Iter 14 — Goal sort (active before completed):**
`GoalRepository.GetAllActiveAsync` → raw SQL `ORDER BY (CompletionDate IS NOT NULL), EnteredDate DESC`. Active goals appear first; completed goals scroll to bottom. Test: `GetAllActiveAsync_ActiveBeforeCompleted`. 21 mobile tests.

**Iter 15 — Dashboard error boundary:**
`DashboardViewModel.LoadAsync` wrapped in try/catch → SyncStatus shows error. Post-sync `RefreshDataAsync` also wrapped separately. 21 mobile tests.

**Iter 16 — SQLite indexes on SyncBase:**
`[Indexed]` on `AccountFk`, `UpdatedOn`, `DeletedAt` in `SyncBase.cs`. sqlite-net-pcl generates unique per-table index names (`<Table>_<Column>`). Indexes created at `CreateTableAsync` time, so new installs and in-memory tests benefit immediately.

**Iter 17 — API: reject future UpdatedOn → 422:**
All 4 sync endpoints: if any record has `UpdatedOn > now + 300_000ms` (5 min), return 422. Prevents clock-skewed clients from poisoning LWW ordering. 4 new Theory tests. 38 API tests pass.

---

## 2026-05-16 — Iteration 13 Brainstorm (fresh — every 3rd)

| # | Description | Dim | Impact | Effort | Risk | Status |
|---|-------------|-----|--------|--------|------|--------|
| 1 | JournalRepository/GoalRepository/TodoRepository: index on AccountFk+DeletedAt | Perf | Medium | S | Low | **DONE (SyncBase)** |
| 2 | API: validate UpdatedOn not in future (> now + 60s) → 422 | Stability | Low | S | Low | **DONE** |
| 3 | GoalListPage: sort active before completed (CompletionDate IS NULL first) | UI | Medium | S | Low | **DONE** |
| 4 | DashboardViewModel: error boundary for RefreshDataAsync | Stability | Medium | S | Low | **DONE** |
| 5 | GoalEntryPage: show ExpirationDate as a read-only label | Func | Low | S | Low | Backlog |
| 6 | SyncService: retry once on transient failure | Stability | Low | M | Medium | Backlog |
| 7 | API: add X-Request-ID header echo for traceability | Perf | Low | S | Low | Backlog |
| 8 | TodoListPage: show count of completed todos at bottom | UI | Low | S | Low | Backlog |
| 9 | GoalListPage: separate section for completed goals | UI | Low | M | Low | Backlog |
| 10 | JournalListPage: pull-to-refresh trigger sync | Func | Medium | M | Medium | Backlog |

---

## 2026-05-16 — Error boundaries in list LoadAsync

**What changed:**
- `JournalListViewModel`, `GoalListViewModel`, `TodoListViewModel`: Added `StatusMessage` observable property. Wrapped `LoadAsync` in try/catch; on exception, sets a user-visible "Could not load..." message. StatusMessage cleared on each successful load.
- `JournalListPage.xaml`, `GoalListPage.xaml`: Wrapped `CollectionView` in `Grid RowDefinitions="Auto,*"`; added red error label as Row 0.
- `TodoListPage.xaml`: Extended existing Grid to `RowDefinitions="Auto,Auto,*"`; added error label as new Row 0, bumped existing rows.

**Why:** On SQLite errors or unexpected nulls, LoadAsync threw silently and the list stayed empty. Users had no signal whether the list was empty (expected) or broken (unexpected).

**Impact:** Silent crash → user-visible status message. 20 mobile tests pass, 34 API tests pass.

**Skipped from brainstorm:**
- #6 (completed goals separate section) — more layout complexity, lower impact than error handling.

---

## 2026-05-16 — Goal completion badge

**What changed:**
- `GoalListPage.xaml`: Added `ColumnDefinitions="*,Auto"` to the row Grid; added right-aligned green "✓ Done" label visible only when `CompletionDate` is non-null.

**Why:** Completed goals remained in the list (correctly — they're soft-deleted only when explicitly deleted) but looked identical to active ones. The badge makes completion visible at a glance.

**Impact:** Completed goals are visually distinct without a separate section.

---

## 2026-05-16 — Iteration 10 Brainstorm

| # | Description | Dim | Impact | Effort | Risk | Status |
|---|-------------|-----|--------|--------|------|--------|
| 1 | SyncService: atomic LastSyncAt (syncStartedAt, not T_end) | Stability | High | S | Low | **DONE** |
| 2 | GoalListPage: completion badge for completed goals | UI | Medium | S | Low | **DONE** |
| 3 | Error boundaries in LoadCommands (catch + status msg) | Stability | Medium | M | Low | **DONE** |
| 4 | GoalEntryPage: ExpirationDate display for existing | Func | Low | S | Low | Backlog |
| 5 | API: validate UpdatedOn not far in future | Stability | Low | M | Low | Backlog |
| 6 | GoalListPage: completed goals separate section | UI | Medium | M | Low | Backlog |
| 7 | SyncService: retry on transient failure | Stability | Low | M | Medium | Backlog |
| 8 | TodoListPage: show completed count / "X done" at bottom | UI | Low | S | Low | Backlog |
| 9 | JournalRepository: index on AccountFk+DeletedAt (query perf) | Perf | Medium | S | Low | Backlog |
| 10 | API: return 422 if UpdatedOn is 0 or negative | Stability | Low | S | Low | Backlog |

---

## 2026-05-16 — Atomic LastSyncAt + mobile test build fix

**What changed:**
- `SyncService.cs`: Added `syncStartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` captured BEFORE any entity syncs. Pass `syncStartedAt` to `UpdateLastSyncAsync` instead of `DateTimeOffset.UtcNow` (which was captured AFTER all syncs complete).
- `ChildDev.Mobile.csproj`: Added `TodoEntryViewModel.cs` to the net8.0 compile-exclusion block (uses `QueryPropertyAttribute` which is MAUI-only; missing exclusion broke net8.0 test builds after the todo tap-to-edit iteration). Added `SkipMauiTargets` conditional to `TargetFrameworks` so CI/test builds can skip the android target without installing MAUI workloads.
- `SyncServiceTests.cs`: Added `RunAsync_Success_UpdatesLastSyncAtToSyncStartTime`, `RunAsync_PartialFailure_DoesNotUpdateLastSyncAt`, `RunAsync_EntitySyncFails_ReturnsFailed`. Fixed pre-existing test `RunAsync_ServerError_ReturnsFailed` → renamed to `RunAsync_HealthFails_ReturnsNoServer` (health 500 correctly returns `NoServer`, not `Failed`) + added `EntitySyncErrorHandler` + separate `RunAsync_EntitySyncFails_ReturnsFailed` test.

**Why:**
- Old code set `LastSyncAt = T_end` (after all syncs complete). Records created/modified during the sync window (T_start ≤ UpdatedOn < T_end) had `UpdatedOn < LastSyncAt`, so they were silently excluded from all future syncs — permanent data loss.
- Fix: `LastSyncAt = T_start`. Records modified during the sync window have `UpdatedOn ≥ T_start`, so they're included in the next sync. Server LWW handles any re-sent overlap records correctly (same UpdatedOn = no change).

**Impact:** Silent data loss window eliminated. Mobile test suite now buildable without MAUI workloads. 20 mobile tests pass, 34 API tests pass (was 17 mobile / 34 API).

**Skipped from brainstorm:**
- #2 (goal completion badge) — UI polish; lower stability ROI than this fix.
- #3 (error boundaries) — Medium effort, next iteration.

---

## 2026-05-16 — Iteration 9 Brainstorm (fresh)

| # | Description | Dim | Impact | Effort | Risk | Status |
|---|-------------|-----|--------|--------|------|--------|
| 1 | JournalListPage: show formatted entry date per item | UI | High | S | Low | **DONE** |
| 2 | GoalListPage: completion badge for completed goals | UI | Medium | S | Low | Backlog |
| 3 | SyncService: atomic LastSyncAt (all-or-nothing) | Stability | High | M | Medium | **DONE (iter 10)** |
| 4 | GoalEntryPage: ExpirationDate field | Func | Low | S | Low | Backlog |
| 5 | Error boundaries in LoadCommands (catch + status msg) | Stability | Medium | M | Low | Backlog |
| 6 | Dashboard: recent journal entries show entry date | UI | Medium | S | Low | **DONE** |
| 7 | API: validate UpdatedOn not far in future | Stability | Low | M | Low | Backlog |
| 8 | GoalListPage: completed goals separate section | UI | Medium | M | Low | Backlog |
| 9 | JournalListPage: date + mood visible at a glance | UI | High | S | Low | **DONE** |
| 10 | SyncService: retry on transient failure | Stability | Low | M | Medium | Backlog |

---

## 2026-05-16 — Journal list entry date display

**What changed:**
- `DueDateConverter.cs`: Added `EntryDateConverter` — formats a unix-ms `long` as "ddd, MMM d" (current year) or "ddd, MMM d yyyy" (past year).
- `App.xaml`: Registered `EntryDateConverter` as `{StaticResource EntryDateConverter}`.
- `JournalListPage.xaml`: Added right-aligned gray date label per row using `EntryDateConverter` on `EnteredDate`. Also gated Mood label with `StringToBoolConverter` (was always visible, now hidden when empty).
- `DashboardPage.xaml`: Added right-aligned date label inside each recent-journal card using the same converter.

**Why:** Journal entries were ordered newest-first but showed no date — users browsing 10+ entries had no way to tell which was written on which day without opening each one. Date as a compact right-aligned label (11sp gray) adds full scannability at near-zero visual cost.

**Impact:** Journal list and dashboard recent entries are now fully scannable by date. 34 API tests pass.

**Skipped from brainstorm:**
- #3 (atomic LastSyncAt) — highest-impact stability item but M effort and medium risk; next iteration.
- #2 (goal completion badge) — deferred; less user-facing than date scannability.

---

## 2026-05-16 — Remove auth token/data leakage via console.log

**What changed:**
- Removed `console.log(this.token)` from `goal.service.ts` constructor — leaked the raw auth token on every service initialization
- Removed `console.log(res2)` from `register.component.ts` save() — leaked the full registration response including `authHash`
- Removed `console.log(this.dataItem)` from `goal-entry.component.ts` save() — leaked goal data including `accountFk`

**Why:** Auth tokens and auth hashes in the browser console are visible to anyone who opens DevTools on a shared/public machine. This is a low-effort, high-impact security fix.

**Impact:** Eliminates 3 sensitive data exposure vectors in the browser console.

**Also fixed:** `todo.service.ts` was using the journal base URL (`/rest/secure/journal`) instead of a todo URL (`/rest/secure/todo`). This was a copy-paste bug that would cause all todo API calls to hit the wrong endpoint.

---

## 2026-05-16 — Fix JWT secret startup-time vs. options-resolution-time inconsistency

**What changed:**
- `ChildDev.Api/Program.cs`: Moved JWT bearer signing key configuration from builder-construction time to ASP.NET Core options-resolution time using `AddOptions<JwtBearerOptions>.Configure<IConfiguration>()`.
- `ChildDev.Api/ChildDev.Api.csproj`: Added `<UseAppHost>false</UseAppHost>` to prevent apphost binary copy conflicts when test builds run.

**Why:** The original code read `CHILDDEV_JWT_SECRET` at `Program.cs` startup (before `WebApplicationFactory.ConfigureAppConfiguration` could inject test values). Separately, it also used `?? throw` which was correct but at the wrong layer. The deferred options pattern ensures: (1) the secret is validated at service resolution time (not silently swallowed), (2) tests can inject config before the secret is read.

**Impact:** Production starts fail fast if `CHILDDEV_JWT_SECRET` is missing. Test factory injection works correctly. The known-placeholder bypass (tokens signed with a public key) is eliminated.

---

## 2026-05-16 — Dashboard refresh after sync + Settings JWT preservation + Cross-account isolation tests

**What changed:**
- `DashboardViewModel.cs`: Extracted `RefreshDataAsync` and call it after successful sync so displayed journals/counts update without re-navigation.
- `AccountService.cs`: Added `SaveServerUrlAsync()` that only updates the URL column, leaving JWT intact.
- `SettingsViewModel.cs`: Use `SaveServerUrlAsync` instead of `SaveServerCredentialsAsync(empty, url)` — the old call was blanking the JWT on every settings save.
- `GoalSyncTests`, `TodoSyncTests`, `GoalProgressSyncTests`: Added `Sync_RecordWithWrongAccountFk_IsRejected` to each — mirrors the isolation test already present in `JournalSyncTests`.

**Why:**
- Dashboard was showing stale data after sync until user left and returned.
- Settings URL save was silently breaking sync by zeroing the JWT.
- Three sync endpoints had no test coverage for cross-account data leakage.

**Impact:** 21 tests pass (was 18). Two silent data/security bugs eliminated.

---

## 2026-05-16 — Sync protocol correctness tests (LWW + delta filtering)

**What changed:**
- Added `Sync_ClientWinsWhenNewerUpdatedOn` and `Sync_ServerWinsWhenNewerUpdatedOn` to Goal and Todo test files (mirroring the Journal tests that already existed).
- Added `Sync_DeltaFiltering_OnlyReturnsRecordsNewerThanLastSyncAt` to Journal, Goal, Todo, and GoalProgress — this test was completely absent for all endpoints.
- Removed empty `UnitTest1.cs` placeholder.
- Added `Sync_ClientWinsWhenNewerUpdatedOn` to GoalProgress.

**Why:** The LWW (last-write-wins) conflict resolution is the core correctness guarantee of the sync protocol. Goal/Todo/GoalProgress endpoints had no coverage for this. The delta filter (`LastSyncAt`) determines which records are returned on each sync — if broken, clients would either miss updates or receive the full history every time.

**Impact:** 29 tests pass (was 21). All 4 sync endpoints now have LWW and delta-filter coverage.

---

## 2026-05-16 — Due-date badge on todo list + sync endpoint N→1 DB query optimization

**What changed:**
- `DueDateConverter.cs`: New `DueDateLabelConverter` (formats `long?` timestamp as "Due today", "Overdue 2d", "Due Fri", etc.) and `DueDateColorConverter` (red=overdue, orange=today, gray=future). Also added `NotNullConverter` (shows element only when value is non-null).
- `App.xaml`: Registered all three new converters as app-level resources.
- `TodoListPage.xaml`: Added right-aligned due-date label with color coding. Visible only when `DueDate` is set.
- All 4 sync endpoints (`GoalEndpoints`, `JournalEndpoints`, `TodoEndpoints`, `GoalProgressEndpoints`): Replaced per-record `FindAsync` call with a single batch `WHERE Guid IN (...)` query + dictionary lookup. Reduces N DB round-trips to 1 per sync call.

**Why:**
- Due dates were set in the entry form but invisible in the list — no urgency signal at a glance.
- Sync endpoint was doing a DB lookup per synced record; a batch of 50 records caused 50 round-trips.

**Impact:** Due-date urgency visible inline. Sync DB load: O(N) queries → O(1) per sync call. All 29 API tests pass.

**Skipped from backlog:**
- #5 (reload on return) — already implemented via `OnAppearing` in all list pages.
- #6 (overdue count on dashboard) — more complex; deferred to later iteration.

---

## 2026-05-16 — Iteration 6 Brainstorm (fresh)

| # | Description | Dim | Impact | Effort | Risk | Status |
|---|-------------|-----|--------|--------|------|--------|
| 1 | Sync: pre-flight /health check before 4 sync calls | Stability | High | S | Low | **DONE** |
| 2 | Settings: "Test" button for server URL connectivity | Func | High | S | Low | **DONE** |
| 3 | TodoList: sort overdue first, then by DueDate | UI | Medium | S | Low | Backlog |
| 4 | API: validate UpdatedOn not far in future | Stability | Medium | M | Low | Backlog |
| 5 | JournalEntryPage: show created date for existing | UI | Low | S | Low | Backlog |
| 6 | Dashboard: overdue todo count with red badge | UI | Medium | M | Low | Backlog |
| 7 | GoalEntryPage: NextMeetingDate label for existing | UI | Low | S | Low | Backlog |
| 8 | API: return 400 if Records list is null/missing | Stability | Low | S | Low | Backlog |
| 9 | SyncService: update LastSyncAt only if all 4 succeed | Stability | High | M | Medium | Backlog |
| 10 | ConnectivityService: use NetworkAccess properly on all platforms | Stability | Low | S | Low | Done (already conditional) |

---

## 2026-05-16 — Goal entry: date display + Mark as Complete button

**What changed:**
- `GoalRepository`: Added `CompleteAsync(guid)` — sets `CompletionDate = now`, updates `UpdatedOn`.
- `GoalEntryViewModel`: Added `IsExisting`, `EnteredDateDisplay` (populated on load), and `MarkCompleteCommand`.
- `GoalEntryPage.xaml`: Shows goal creation date at top for existing goals; "Mark as Complete" green button visible only for existing goals.

**Why:** Users had no way to mark a goal complete from the edit page. Also mirrored the Journal pattern of showing the creation date, so users can see when they set the goal.

**Impact:** Goals can be completed from the entry form. Entry date visible for context. 34 tests pass.

---

## 2026-05-16 — Journal entry date display + Dashboard overdue todo count

**What changed:**
- `JournalEntryViewModel`: Added `EnteredDateDisplay` property — populated on load with `"ddd, MMM d yyyy"` format. Empty string for new entries.
- `JournalEntryPage.xaml`: Shows `EnteredDateDisplay` as a gray label at the top, visible only for existing entries (StringToBoolConverter on empty string).
- `DashboardViewModel`: Added `OverdueTodoCount` and `HasOverdueTodos` — computed from pending todos with `DueDate < now`.
- `DashboardPage.xaml`: Red "N overdue" subtitle in the Pending Todos tile, visible only when `HasOverdueTodos` is true.

**Why:**
- Journal entries had no visible date — users editing past entries had no context for when they were written.
- Dashboard showed total pending but not urgency; "3 overdue" in red draws attention to items that need action.

**Impact:** Journal entry context visible. Dashboard urgency signal added. 34 tests pass.

---

## 2026-05-16 — Iteration 8 Brainstorm (fresh)

| # | Description | Dim | Impact | Effort | Risk | Status |
|---|-------------|-----|--------|--------|------|--------|
| 1 | API: null Records guard → 400 | Stability | High | S | Low | **DONE** |
| 2 | API: OrderBy(UpdatedOn) on delta response | Stability | Medium | S | Low | **DONE** |
| 3 | Dashboard: overdue todo count with red badge | UI | Medium | M | Low | Backlog |
| 4 | API: validate UpdatedOn not far in future | Stability | Low | M | Low | Backlog |
| 5 | JournalEntryPage: show creation date | UI | Low | S | Low | Backlog |
| 6 | SyncService: atomic LastSyncAt (all-or-nothing) | Stability | High | M | Medium | Backlog |
| 7 | GoalEntryPage: show NextMeetingDate label if existing | UI | Low | S | Low | Backlog |
| 8 | GoalListPage: separate completed goals visually | UI | Low | M | Low | Backlog |
| 9 | TodoListPage: show "completed" bottom section | UI | Low | M | Low | Backlog |
| 10 | API: ETag/conditional-get for sync | Perf | Low | L | Medium | Backlog |

---

## 2026-05-16 — API sync input validation + deterministic delta ordering

**What changed:**
- All 4 sync endpoints (`/api/sync/{journal,goal,goal-progress,todo}`): Added `if (req.Records is null) return Results.BadRequest(...)` guard before accessing Records. Prevents NullReferenceException (→ 500) when malformed client sends `"Records": null`.
- All 4 sync endpoints: Added `.OrderBy(t => t.UpdatedOn)` to the delta response query. Delta records now arrive at the client in chronological order — predictable and easier to debug.
- `SyncInputValidationTests.cs`: Theory test across all 4 endpoints verifying null Records → 400.

**Impact:** 34 tests (was 30). Null input no longer causes 500. Delta ordering is deterministic.

---

## 2026-05-16 — TodoList sort: overdue first, nulls last

**What changed:**
- `TodoRepository.GetPendingAsync`: Changed LINQ `.OrderBy(t => t.DueDate)` (null-first in SQLite ASC) to raw SQL `ORDER BY (DueDate IS NULL), DueDate`. Boolean expression evaluates 0 (non-null) before 1 (null), so todos without a due date sort last; overdue items appear first among dated todos.

**Why:** Previously, todos with no due date appeared at the top because SQLite sorts NULL first in ascending order. Overdue items were buried below undated ones — the opposite of urgency ordering.

**Impact:** Most urgent todos (overdue, then soonest due, then undated) appear first. 30 API tests pass.

---

## 2026-05-16 — Settings "Test Connection" + SyncService pre-flight health check

**What changed:**
- `SettingsViewModel`: Injected `IHttpClientFactory`; added `TestConnectionCommand` — GETs `{serverUrl}/health` with 5s timeout; sets StatusMessage to "Connected!", "Server error: {code}", or "Cannot reach server."
- `SettingsPage.xaml`: Split save row into a 2-button Grid — "Save Server URL" + "Test" side by side.
- `SyncService`: Added pre-flight `GET health` before the 4 sync entity calls. Non-200 response returns `SyncResult.NoServer` immediately — avoids 4 failing HTTP calls on an unreachable server and distinguishes "server down" from "sync logic error".

**Why:** Users had no way to verify their server URL was correct without triggering a full sync and waiting for the timeout. The pre-flight check also makes sync error classification more accurate.

**Impact:** Settings page confirms connectivity in <5s. Sync skips 4 wasted requests when server is unreachable. 30 API tests pass.

---

## 2026-05-16 — API health endpoint + GoalListPage MeasurableOutcome subtitle

**What changed:**
- `Program.cs`: Added `GET /health` endpoint returning `{"status":"ok","utc":"..."}` — no auth required. Useful for deployment health probes and mobile app connectivity checks.
- `HealthEndpointTests.cs`: Verified the endpoint returns 200. (30 tests now pass, was 29.)
- `GoalListPage.xaml`: Added MeasurableOutcome as a gray subtitle below each goal, visible only when set (mirrors JournalListPage Mood display pattern).

**Why:**
- No way to check if the API was reachable without triggering a full sync. Health check is the standard solution.
- Goals showed only text; the measurable outcome is the key success criterion for each goal — it should be visible at a glance.

**Impact:** 30 tests (was 29). Deployment monitoring now possible. Goals list shows success criteria inline.

---

## 2026-05-16 — Dashboard navigation tiles + TodoEntryPage "Mark Done"

**What changed:**
- `DashboardViewModel`: Added `GoToGoalsCommand` (navigates `//goals`) and `GoToTodosCommand` (navigates `//todos`).
- `DashboardPage.xaml`: Added `TapGestureRecognizer` to "Active Goals" and "Pending Todos" Border tiles.
- `TodoEntryViewModel`: Added `IsExisting` flag (set to true after loading an existing todo), and `MarkDoneCommand` that calls `CompleteAsync` and navigates back.
- `TodoEntryPage.xaml`: Added green "Mark as Done" button, visible only for existing todos (`IsVisible="{Binding IsExisting}"`).

**Why:**
- Count tiles on the dashboard were prominent but non-interactive — natural affordance was broken.
- Users had to navigate back to the list and swipe-left to complete a todo after editing it.

**Impact:** Dashboard tiles are now navigation shortcuts. Todos can be completed directly from the edit page. All 29 API tests pass.

---

## 2026-05-16 — Iteration 3 Brainstorm (fresh)

| # | Description | Dim | Impact | Effort | Risk | Status |
|---|-------------|-----|--------|--------|------|--------|
| A | Empty-state messages for all list views | UI | High | S | Low | **DONE** |
| B | Dashboard: parallel data load (Task.WhenAll) | Perf | Low | S | Low | Skipped (SQLite serializes anyway) |
| C | Dashboard count tiles tap to navigate to list | UI | Medium | S | Low | Backlog |
| D | GoalListPage: show MeasurableOutcome subtitle | UI | Low | S | Low | Backlog |
| E | TodoEntryPage: "Mark Done" button | Func | Medium | S | Low | Backlog |
| F | DashboardPage: overdue todo count with red badge | UI | Medium | M | Low | Backlog |
| G | Error boundary in LoadCommand (catch + user-visible message) | Stability | Medium | M | Low | Backlog |
| H | API: health-check endpoint `/health` | Stability | Low | S | Low | Backlog |
| I | JournalEntryPage: show entry date for existing journals | UI | Low | S | Low | Backlog |
| J | GoalListPage: show completion badge for completed goals | UI | Low | S | Low | Backlog |

---

## 2026-05-16 — Empty-state messages for all list views

**What changed:**
- `JournalListPage.xaml`: Added `CollectionView.EmptyView` — "No journal entries yet. Tap + to write your first entry."
- `GoalListPage.xaml`: Added `CollectionView.EmptyView` — "No goals yet. Tap + to set your first goal."
- `TodoListPage.xaml`: Added `CollectionView.EmptyView` — "All done! Type a task above and tap Add."
- `DashboardPage.xaml`: Added `CollectionView.EmptyView` for recent journals section.

**Why:** Empty lists showed no content and no guidance. New users had no affordance indicating what to do or that the list was genuinely empty (not still loading).

**Impact:** First-run UX improved. All 29 API tests pass.

**Skipped:** Dashboard parallel load — SQLite-net-pcl serializes all operations through a single connection thread; `Task.WhenAll` provides no actual parallelism. Marked skipped with reasoning.

---

## 2026-05-16 — Brainstorm Backlog (Iteration 1)

| # | Description | Dim | Impact | Effort | Risk | Status |
|---|-------------|-----|--------|--------|------|--------|
| 1 | Tap-to-edit todo list items (TodoEntryPage) | UI/Func | High | S | Low | **DONE** |
| 2 | Show notes preview on todo list items | UI | Medium | S | Low | **DONE** (bundled with #1) |
| 3 | GoalListPage/JournalListPage TapGestureRecognizer move to correct nesting | UI | Medium | S | Low | **DONE** (pre-existing, bundled with #1) |
| 4 | GoalEntryViewModel: load NextStepItems from progress repo | Func | Medium | S | Low | **DONE** (pre-existing, bundled with #1) |
| 5 | TodoListPage: reload list after returning from entry (OnAppearing) | UI | Medium | S | Low | Next iteration |
| 6 | DashboardPage: show overdue todo count | UI/Func | Medium | M | Low | Backlog |
| 7 | Add due-date display badge on todo list items | UI | Medium | S | Low | Backlog |
| 8 | API: add pagination to list endpoints (large data sets) | Perf | Medium | M | Medium | Backlog |
| 9 | Add empty-state messages to CollectionViews | UI | Low | S | Low | Backlog |
| 10 | GoalListPage: show measurable outcome as subtitle | UI | Low | S | Low | Backlog |

---

## 2026-05-16 — Tap-to-edit for Todo list items + Notes preview

**What changed:**
- Created `TodoEntryPage.xaml` + `TodoEntryPage.xaml.cs` — entry/edit page for todo items mirroring GoalEntry/JournalEntry pattern.
- Created `TodoEntryViewModel.cs` — `[QueryProperty]`-based VM with Title, Notes, DueDate (optional), and SaveCommand.
- Added `TodoRepository.GetAsync(guid)` — public accessor so the VM can load an existing todo for editing.
- Added `OpenCommand` to `TodoListViewModel` — navigates to `todos/entry?guid=...`.
- Updated `TodoListPage.xaml` — wraps content in a tappable Grid with TapGestureRecognizer; shows Notes as a subtitle when present (via `StringToBoolConverter`).
- Registered `todos/entry` route in `AppShell.xaml.cs`.
- Registered `TodoEntryViewModel` and `TodoEntryPage` in `MauiProgram.cs`.
- Also included pre-existing uncommitted changes: GoalListPage/JournalListPage TapGestureRecognizer correctly nested, GoalEntryViewModel loads NextStepItems.

**Why:** Todos were the only list without tap-to-edit. Users had no way to correct a misspelled title or add notes after creation — only swipe-to-delete existed.

**Impact:** Full CRUD for todos. Notes preview visible inline on the list.

**Skipped from brainstorm:**
- #5 (reload after entry) — low effort but separate iteration to keep diff focused.
- #6–#10 — lower impact-to-effort ratio than #1.

---

## Flagged but not implemented (requires backend coordination)

**Password in URL:** `account.service.ts` `token()` method sends the password as a plain path segment in a GET request (`/token/{nickname}/{password}`). Passwords in URLs are logged by servers, proxies, and browser history. Fix requires a POST-based authentication endpoint on the backend.

---

## Pre-existing lint errors (not introduced by this session)

Multiple `azAuthHeader` quoting, line-length, and semicolon issues across `goal.service.ts`, `journal.service.ts`, and `todo.service.ts`. These pre-date this session and are cosmetic — tracked for a future focused lint-cleanup pass.
