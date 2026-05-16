# Improvement Log

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
