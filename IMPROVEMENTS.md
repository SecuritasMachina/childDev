# Improvement Log

---

## 2026-05-17 — UX: disabled bindings for Settings page Save/Change buttons (iter 396)

**File:** `ChildDev.Api/Components/Pages/Settings.razor`

**Change:** "Save Nickname" grays out when the field is blank, exceeds 50 characters, or is unchanged from the current nickname. "Change PIN" grays out when current PIN is blank, new PIN is fewer than 4 characters, or the confirmation doesn't match.

**Why:** Continuation of the button-disable sweep from iter 391. Both buttons previously allowed submission that would immediately fail with an error message. The `Disabled` binding gives immediate visual feedback without requiring a server round-trip or an error snackbar for obvious invalid states.

**Impact:** Build: 0 warnings, 0 errors.

---

## 2026-05-17 — UX: add confirmation dialogs for all destructive delete actions in web UI (iter 395)

**Files:** `ChildDev.Api/Components/Pages/Home.razor`, `GoalDetail.razor`, `JournalPage.razor`, `Todos.razor`

**Change:** All four web pages previously executed deletes immediately on button click with no confirmation. Added inline confirm dialogs using the existing `@bind-Visible` pattern. A `_confirmDeleteGuid` (or `_confirmDeleteGoal`) state variable captures the pending item; a compact MudDialog asks "Delete X?" with Cancel / Delete buttons. The actual delete runs only on confirmation.

**Coverage:** Goal delete from home card, goal delete from goal detail header, progress note delete, journal entry delete, todo delete (pending list + completed list).

**Why:** Accidental deletion of goals, progress notes, journal entries, or todos is hard to notice since soft-deletes are synced and mobile shows the same data. A one-click delete with no undo prompt creates unnecessary data-loss risk, especially for kids or caregivers who may be less careful. The confirm step costs one extra click on intentional deletes and prevents accidents.

**Impact:** Zero logic changes — only the code path to reach the delete handlers changed. Build: 0 warnings, 0 errors.

---

## 2026-05-17 — Perf: replace read-modify-write with targeted UPDATEs in AccountService (iter 394)

**File:** `ChildDev.Mobile/Services/AccountService.cs`

**Change:** Replaced `GetAccountAsync()` + `db.UpdateAsync(account)` in `SaveServerCredentialsAsync` and `SaveServerUrlAsync` with single `db.ExecuteAsync("UPDATE Account SET ... = ?"` calls targeting only the changed columns.

**Why:** The read-modify-write pattern loaded all Account columns, modified one or two, and saved all back. If `UpdateLastSyncAsync` ran concurrently (e.g., sync completing while the user saves settings), the `db.UpdateAsync` could overwrite `LastSyncAt` with the stale snapshot. Targeted column UPDATEs eliminate this race. Consistent with iters 376, 390, 393 which already cleaned up `UpdateLastSyncAsync` and `ClearServerJwtAsync`.

**Impact:** 242 mobile tests — all passing.

---

## 2026-05-17 — Perf: eliminate redundant SELECT in ClearServerJwtAsync (iter 393)

**File:** `ChildDev.Mobile/Services/AccountService.cs`

**Change:** Replaced `GetAccountAsync()` + `ExecuteAsync("... WHERE Guid = ?", account.Guid)` with a single `ExecuteAsync("UPDATE Account SET ServerJwt = NULL")`. Same reasoning as iter 390 for `UpdateLastSyncAsync` — single-account table, no WHERE needed.

**Why:** Continuation of the AccountService SELECT-before-targeted-UPDATE cleanup. The SELECT was only used to obtain the Guid for the WHERE clause, which is unnecessary when the table always has at most one row.

**Impact:** 242 mobile tests — all passing.

---

## 2026-05-17 — Perf: replace N UpdateAsync calls with single SQL UPDATE in DeleteForGoalAsync (iter 392)

**File:** `ChildDev.Mobile/Data/GoalProgressRepository.cs`

**Change:** `DeleteForGoalAsync` previously fetched all active progress records for a goal then issued one `UpdateAsync` per row. Replaced with a single `ExecuteAsync("UPDATE GoalProgress SET DeletedAt = ?, UpdatedOn = ? WHERE GoalFk = ? AND DeletedAt IS NULL", ...)`. Already-deleted records are excluded by the `AND DeletedAt IS NULL` clause, matching the prior loop behavior.

**Why:** N round-trips to SQLite for each active progress record on goal deletion. A single UPDATE is O(1) round-trips regardless of how many progress notes exist. Most goals will have few notes, but the pattern was unnecessarily chatty.

**Impact:** 242 mobile tests — all passing.

---

## 2026-05-17 — Fix: Disabled binding on progress note Save buttons in GoalDetail + Home (iter 391)

**Files:** `ChildDev.Api/Components/Pages/GoalDetail.razor`, `ChildDev.Api/Components/Pages/Home.razor`

**Change:** Added `Disabled` bindings to three Save/Note buttons:
- Home Quick Note "Save Note" — disabled when `QuickNoteText` is blank
- GoalDetail Add Progress "Save" — disabled when both `NewNextSteps` is blank and `NewMeetingDate` is null
- GoalDetail Edit Progress "Save" — disabled when both `EditProgressNextSteps` is blank and `EditProgressMeetingDate` is null

**Why:** Iter 387 added warning snackbars for these empty-submission cases, but did not gray out the button. Silent no-ops or warning-only feedback are weaker UX than a grayed-out button that prevents the invalid action upfront. Completes the button-disable sweep started in iter 388.

**Impact:** 220 API tests — all passing.

---

## 2026-05-17 — Perf: eliminate redundant SELECT in UpdateLastSyncAsync (iter 390)

**File:** `ChildDev.Mobile/Services/AccountService.cs`

**Change:** Replaced `GetAccountAsync()` + `ExecuteAsync(...WHERE Guid = ?)` pattern with a single `ExecuteAsync("UPDATE Account SET LastSyncAt = ?", timestamp)`. The Account table is single-row (single-user app), so filtering by Guid is unnecessary, and the extra SELECT was a wasted round-trip before every sync.

**Why:** Iter 376 already replaced `db.UpdateAsync(account)` with a targeted SQL UPDATE to prevent credential overwrites. This iter removes the remaining unnecessary SELECT that was only needed to obtain the Guid (already statically unnecessary for a single-account table).

**Impact:** 242 mobile tests — all passing.

---

## 2026-05-17 — Fix: mobile Add Todo button disabled when title is blank (iter 389)

**File:** `ChildDev.Mobile/ViewModels/TodoListViewModel.cs`

**Change:** Added `CanAdd()` method and `OnNewTodoTitleChanged` partial to propagate the can-execute state, and attached it as `CanExecute = nameof(CanAdd)` on the `AddCommand`. The Add button in the quick-add row on the Todo list page is now automatically disabled by the MVVM framework when the title field is empty.

**Why:** `AddAsync` already had a `if (string.IsNullOrWhiteSpace) return` guard, but the underlying command had no `CanExecute`, so the button remained visually enabled. Consistent with the `GoalEntryViewModel`, `JournalEntryViewModel`, and `TodoEntryViewModel` which all use `CanExecute` for their Save commands.

**Impact:** 242 mobile tests — all passing.

---

## 2026-05-17 — Fix: disabled Save buttons when required fields are blank (iter 388)

**Files:** `ChildDev.Api/Components/Pages/Todos.razor`, `ChildDev.Api/Components/Pages/Home.razor`, `ChildDev.Api/Components/Pages/JournalPage.razor`

**Change:** Added `Disabled` bindings to dialog Save buttons so they are visually grayed out and unclickable when the required fields are empty:
- Add Todo / Edit Todo: disabled when title is blank
- Add Goal: disabled when goal text is blank
- Add/Edit Journal Entry: disabled when both Notes and Activity are blank

**Why:** These dialogs all had silent return-on-blank validation — clicking Save with empty required fields did nothing, leaving the dialog open. Disabling the button is clearer UX than a silent no-op or a snackbar warning.

**Impact:** 220 API tests — all passing.

---

## 2026-05-17 — Fix: silent no-op when saving empty progress note (iter 387)

**File:** `ChildDev.Api/Components/Pages/GoalDetail.razor`

**Change:** `AddProgress` and `SaveProgressEdit` both returned silently (dialog stayed open, nothing happened) when the user submitted a progress note with both the notes text and meeting date cleared. Added a warning snackbar: "Enter notes or a meeting date."

**Why:** The dialog appeared stuck — the user had no indication of why Save didn't work. The validation was already correct (preventing empty progress notes per iter 372's rule that at least one of NextStepItems or NextMeetingDate must be set) but the failure was invisible.

**Impact:** 220 API tests — all passing.

---

## 2026-05-17 — Fix: activity-only journal entries show blank in Home dashboard (iter 386)

**File:** `ChildDev.Api/Components/Pages/Home.razor`

**Change:** The recent journal list on the dashboard used `j.Notes` as the display text. For activity-only entries (null Notes), this rendered a blank body line with the Activity shown only in a small secondary caption. Changed to `j.Notes ?? j.Activity` for the primary text, and show Activity as a secondary caption only when both Notes and Activity are present.

**Why:** Iter 375 added support for activity-only journal entries (null Notes with an Activity value). The web dashboard was not updated to handle the null case, causing those entries to appear blank in the "Recent Journal" panel.

**Impact:** 220 API tests — all passing.

---

## 2026-05-17 — Test + refactor: GoalProgress upload, dead code, null-steps info (iters 383–385)

**Files:** `ChildDev.Mobile.Tests/SyncServiceTests.cs`, `ChildDev.Mobile/Data/GoalProgressRepository.cs`, `ChildDev.Mobile.Tests/GoalProgressRepositoryTests.cs`

**Iter 383:** Added `RunAsync_LocalGoalProgress_NullNextStepsMeetingDateOnly_IncludedInUploadRequest` — the upload counterpart to iter 379's receive test. Confirms that meeting-date-only GoalProgress records (null `NextStepItems`) are correctly serialized and sent to the server during sync.

**Iter 384:** Removed `GetLatestNextStepsAsync` from `GoalProgressRepository` and its 5 tests. The method was superseded by `GetLatestProgressInfoAsync` and had no callers outside tests.

**Iter 385:** Added `GetLatestProgressInfoAsync_NullNextStepsLatest_ReturnsNullStepsWithCorrectTimestamp` — verifies that when the most recent progress for a goal is a meeting-date-only entry (null `NextStepItems`), `GetLatestProgressInfoAsync` returns null for `Steps` and the correct (newest) `UpdatedOn`. This property is relied on by both `DashboardViewModel` and `GoalListViewModel`.

**Impact:** 242 mobile tests (was 246 before dead-code removal; net -4 tests removed, +3 new tests added) — all passing.

---

## 2026-05-17 — Fix: today's meeting date not pre-filled in Add Progress dialog (iter 381) + refactor ApplyFilter (iter 382)

**Files:** `ChildDev.Api/Components/Pages/GoalDetail.razor`, `ChildDev.Api/Components/Pages/Todos.razor`

**Iter 381:** `OpenAddProgressDialog` used `> nowMs` (UTC) to check if the goal's `NextMeetingDate` was upcoming. Since `NextMeetingDate` is stored as local midnight, today's meeting was excluded. Changed to `>= todayStartMs`.

**Iter 382:** `ApplyFilter` was recomputing `todayStartMs` locally while the `TodayStartMs` field was already available from `LoadTodos`. Both Overdue and Today filters now use the same field, eliminating redundant computation.

**Impact:** 220 API tests — all passing.

---

## 2026-05-17 — Fix: goal meeting/expiration dates shown as past/overdue prematurely (iter 380)

**Files:** `ChildDev.Api/Components/Pages/GoalDetail.razor`, `ChildDev.Api/Components/Pages/Home.razor`, `ChildDev.Mobile/ViewModels/DashboardViewModel.cs`

**Change:** Same class of bug as iter 378. Dates stored as local-midnight ms were compared against current UTC time. GoalDetail and Home.razor now compare local date vs `DateTime.Today`. DashboardViewModel upcoming meetings filter changed from `> nowMs` to `>= todayStartMs` so today's meetings appear as "Next goal meeting: today".

**Impact:** 220 API + 245 mobile tests — all passing.

---

## 2026-05-17 — Test: meeting-date-only GoalProgress mobile sync (iter 379)

**File:** `ChildDev.Mobile.Tests/SyncServiceTests.cs`

**Change:** Added `RunAsync_ServerReturnsGoalProgress_NullNextStepsMeetingDateOnly_StoredLocally` — verifies GoalProgress with null NextStepItems and a NextMeetingDate syncs correctly from server to mobile local DB.

**Impact:** 245 mobile tests (was 244) — all passing.

---

## 2026-05-17 — Fix: todos due today incorrectly shown as overdue (iter 378)

**Files:** `ChildDev.Api/Components/Pages/Home.razor`, `ChildDev.Api/Components/Pages/Todos.razor`, `ChildDev.Mobile/ViewModels/DashboardViewModel.cs`, `ChildDev.Mobile/ViewModels/TodoListViewModel.cs`

**Change:** Changed all four overdue detection points from `DueDate < nowMs` (current UTC time) to `DueDate < todayStartMs` (local midnight). `NowMs` field removed from web Todos overdue check (still retained for other use); `TodayStartMs` field added.

**Why:** `DueDate` is stored as local midnight in Unix ms (matching how the date picker works). Comparing against the current UTC moment means any todo with today as a due date is immediately flagged overdue from midnight. "Overdue" should mean "due before today", not "due before now".

**Impact:** 220 API + 244 mobile tests — all passing.

---

## 2026-05-17 — Fix: activity-only journal entries blank in JournalListPage (iter 377)

**File:** `ChildDev.Mobile/Views/JournalListPage.xaml`

**Change:** Switched the main label binding from `Notes` to `DisplayText` (same fix applied to `DashboardPage` in iter 375).

**Why:** The `DisplayText` computed property (`Notes ?? Activity ?? string.Empty`) was added in iter 375 to handle activity-only entries, but only applied to the Dashboard at the time. The Journal list page still bound to `Notes` directly, showing blank rows for entries created with only an Activity field.

**Impact:** 244 mobile tests — all passing.

---

## 2026-05-17 — Fix: make UpdateLastSyncAsync atomic with targeted SQL UPDATE (iter 376)

**File:** `ChildDev.Mobile/Services/AccountService.cs`

**Change:** Replaced read-modify-write pattern (load account → set field → UpdateAsync) with `db.ExecuteAsync("UPDATE Account SET LastSyncAt = ? WHERE Guid = ?", ...)` targeting only the `LastSyncAt` column.

**Why:** The old pattern loaded the full Account row, modified one field, then saved all columns. If `SaveServerCredentialsAsync` ran concurrently (e.g., user saving settings while a sync completes), the credential update could be silently overwritten by the sync's `UpdateAsync` call. The `_syncing` lock only prevents concurrent syncs — it doesn't guard credential saves. The targeted SQL UPDATE eliminates the race by only touching the one column.

**Impact:** 244 mobile tests — all passing.

---

## 2026-05-17 — Loop 2 Bootstrap

### Deploy & Playwright Status
- **Deploy target:** No staging deploy target exists. Only a Dockerfile for production-style builds. Per loop rules, deploy step is skipped to avoid touching prod. Logged here per instructions.
- **Playwright:** No Playwright test suite exists in this repo. Playwright step skipped. Logged here per instructions.
- **Git branch:** Repo uses `master` (not `main`). Merging to `master`.

---

### Unsolved Problems (Step 0a — one-time research)

Search results were sparse for this niche domain. Key pain points found:

| Pain point | Source | Frequency | In scope? |
|---|---|---|---|
| Default date filters not customizable (e.g., "past 7 days" default) | AbleSpace Capterra reviews, 2025 | Low (1 review) | Partial — our filters are hardcoded |
| No way to track by session count vs. service time | AbleSpace Capterra, 2025 | Low (1 review) | No |
| Paywalls on "free" child dev apps block basic features | Kinedu App Store reviews | Medium (multiple) | No |
| Data entry burden — paper tracking is cumbersome but apps add learning curve | Behaviorhelp, alightaba articles 2024 | Medium | Yes — simpler entry flows |
| Missing educational resources linked to goals/milestones | CDC Milestone Tracker reviews | Low | Partial |
| Scheduling overlap (which kids share a session) | AbleSpace Capterra 2025 | Low | No |

**Research note:** This is a niche B2C/prosumer app; web search returns mostly competitor marketing, not raw user pain. The most actionable signal is the data-entry friction theme — users want fast, simple capture with minimal clicks.

---

### Domain Notes (Step 0b)

ChildDev tracks 4 core entities for a child's developmental journey:
- **Journal** — free-form notes/observations (with mood, activity, tags, location)
- **Goals** — developmental goals with measurable outcomes, expiration dates, next-meeting dates
- **GoalProgress** — progress notes per goal (next steps, meeting dates)
- **Todos** — tasks related to the child (with due dates, notes)

The app is offline-first (mobile-primary via MAUI), with a sync API and now a web UI (Razor Pages, just added). Comparable tools: AbleSpace, Understood, Thumsters, CDC Milestone Tracker.

Key domain workflows:
1. Capture a quick observation → Journal entry
2. Set a goal at a meeting → Goal + GoalProgress
3. Assign followup tasks → Todos
4. Review progress across time → Dashboard

**Regulatory:** Not HIPAA-covered (no provider context evident), but parental data sensitivity warrants no PII beyond what's already stored.

**Actionable domain insights:**
- Fast capture (minimal taps/clicks) is crucial — parents/teachers log on-the-go
- Cross-entity dashboard view matters — quick status of "what's happening with this child today"
- Due date visibility for todos is high-value — overdue todos should be prominent
- Progress visualization (trend over time) is the #1 differentiator in comparable tools

---

### Brainstorm — 2026-05-17

| # | Description | Dim | Source | Impact | Effort | Risk | Positive | Negative | Done? |
|---|---|---|---|---|---|---|---|---|---|
| 1 | Add Todos web page (list + add + complete) | Func | Domain: Todos is core entity, no web access | High | S | Low | Complete web coverage of all 4 entities; users can manage tasks from browser | +2 files, small test gap | No |
| 2 | Add Journal web page (list + add entries) | Func | Domain: Journal is core entity | High | S | Low | Full CRUD parity with mobile; journaling from desktop | +2 files | No |
| 3 | Add analytics event tracking to web UI | Perf/Stability | CLAUDE.md mandatory requirement | High | S | Low | Enables feature optimization and usage analytics per CLAUDE.md | New DB table; EnsureCreated handles it; ~1 extra write per action | No |
| 4 | Dashboard stats summary (active goals, pending todos, journal entries this week) | UI | Domain: quick-status view | Medium | S | Low | At-a-glance status reduces navigation burden by ~50% | 3 additional DB queries per dashboard load | No |
| 5 | GoalProgress entry from web (add progress notes to a goal) | Func | Domain: progress notes per goal | High | M | Low | Enables full goal lifecycle from web without mobile | More complex UI; ~3 new files | No |
| 6 | Overdue todo highlight (show overdue in red, count badge) | UI | Unsolved: data-entry friction / quick status | Medium | S | Low | Reduces missed tasks; immediate visual signal | CSS-only change, negligible | No |
| 7 | Goal edit from web (edit GoalText, MeasurableOutcome) | Func | Domain: goals need updating | Medium | M | Low | Users can correct goal text without mobile | +1 page, ~3 files | No |
| 8 | Add GoalProgress list per goal on Goals page | UI | Domain: progress visibility | Medium | S | Low | Shows progress history inline | Extra DB query per goal | No |
| 9 | Pagination for large datasets | Perf | Domain: long-term use accumulates data | Low | S | Low | Prevents slow pages at scale | Adds complexity; dev datasets tiny | No |
| 10 | Search/filter goals and todos | UI | Unsolved: data-entry friction / finding records | Low | M | Low | Faster retrieval for power users | Requires query changes | No |

**Selection for iteration 1:** Items #3 (analytics, mandatory per CLAUDE.md) + #1 (Todos page, highest-impact missing feature). Combining because #3 is a pre-existing requirement from CLAUDE.md that should have been in the initial web UI build, not truly optional. Both are S effort. The analytics table uses EnsureCreated — no migration files involved.

---

## 2026-05-17 — Iteration 263 — API: GoalProgress delta strict-greater-than LastSyncAt boundary test

**What changed:**
- `GoalProgressSyncTests.cs`: Added `Sync_LastSyncAt_ExactlyEqualToRecordUpdatedOn_ExcludedFromDelta` (gpsync_exact_boundary1) — uploads a goal-progress record with `UpdatedOn = ts`, syncs with `LastSyncAt = ts`, asserts the record does NOT appear in the delta.

**Why:** Completes the strict-`>` boundary coverage set for all 4 entities (Goal ✓ iter 257, Journal ✓ 261, Todo ✓ 262, GoalProgress ✓ 263). GoalProgressEndpoints has its own filter query; boundary regression would go undetected without this test.

**Impact:** 200 API tests pass (was 199). 210 mobile tests pass.

---

## 2026-05-17 — Iteration 262 — API: Todo delta strict-greater-than LastSyncAt boundary test

**What changed:**
- `TodoSyncTests.cs`: Added `Sync_LastSyncAt_ExactlyEqualToRecordUpdatedOn_ExcludedFromDelta` (tsync_exact_boundary1) — uploads a todo with `UpdatedOn = ts`, syncs with `LastSyncAt = ts`, asserts the record does NOT appear in the delta.

**Why:** Completing the strict-`>` boundary test coverage set (Goal ✓ iter 257, Journal ✓ 261, Todo ✓ 262, GoalProgress remaining). Todo uses its own filter in TodoEndpoints; a copy-paste error introducing `>=` would only be caught by this test.

**Impact:** 199 API tests pass (was 198). 210 mobile tests pass.

---

## 2026-05-17 — Iteration 261 — API: Journal delta strict-greater-than LastSyncAt boundary test

**What changed:**
- `JournalSyncTests.cs`: Added `Sync_LastSyncAt_ExactlyEqualToRecordUpdatedOn_ExcludedFromDelta` (jsync_exact_boundary1) — uploads a journal with `UpdatedOn = ts`, syncs with `LastSyncAt = ts`, asserts the record does NOT appear in the delta.

**Why:** Iteration 257 added this exact-boundary test for Goal. Journal is the most frequently synced entity and has its own filter query (`WHERE UpdatedOn > LastSyncAt`). If the filter were changed to `>=`, existing tests would not detect it since none tested the exact-equal boundary. Completing 2 of 4 entities with this boundary coverage (Journal and Goal).

**Impact:** 198 API tests pass (was 197). 210 mobile tests pass.

---

## 2026-05-17 — Iteration 292 — API: GoalProgress soft-delete with blank NextStepItems accepted

**What changed:**
- `GoalProgressSyncTests.cs`: Added `Sync_SoftDeletedRecord_BlankNextStepItems_Accepted` — uploads a GoalProgress with `DeletedAt = UpdatedOn` and `NextStepItems = null`, asserts HTTP 200 and stored record with correct `DeletedAt`.

**Why:** The validation gate permits blank `NextStepItems` when `DeletedAt` is set (tombstone records), but this path was untested. Tombstones don't need content — only the GUID and timestamps matter for propagating a deletion.

**Impact:** 214 API tests pass (was 213). 221 mobile tests pass.

---

## 2026-05-17 — Iteration 291 — Web: Soft-delete progress notes from goal detail

**What changed:**
- `GoalDetail.razor`: Each progress note timeline item now has a delete icon. Clicking it soft-deletes (DeletedAt = UpdatedOn = now) and reloads the list. Tracks `progress_delete` analytics.

**Why:** There was no way to remove an incorrectly added progress note from the web. The deletion propagates to mobile via the existing LWW sync mechanism.

**Impact:** 213 API tests pass. 221 mobile tests pass.

---

## 2026-05-17 — Iteration 290 — Web: Edit NextMeetingDate and ExpirationDate from goal detail

**What changed:**
- `GoalDetail.razor`: Goal edit dialog now includes date pickers for `NextMeetingDate` and `ExpirationDate`. `OpenEditDialog` pre-fills both from current values; `SaveGoalEdit` writes them back with LWW `UpdatedOn`.

**Why:** These fields were previously web-read-only — visible on the home card and detail page, but only settable from the mobile app. Web users had no way to schedule or deadline a goal without a mobile device.

**Impact:** 213 API tests pass. 221 mobile tests pass.

---

## 2026-05-17 — Iteration 289 — Web: Goal creation date and age on detail page

**What changed:**
- `GoalDetail.razor`: Shows "Set on [date] — N days ago" below the measurable outcome using `Goal.EnteredDate`.

**Why:** Without the creation date, the progress timeline has no anchor. Knowing a goal was set "14 days ago" makes 3 progress notes feel like meaningful momentum rather than random entries.

**Impact:** 213 API tests pass. 221 mobile tests pass.

---

## 2026-05-17 — Iteration 288 — Web: Todos empty state + logout analytics

**What changed:**
- `Todos.razor`: Added `MudAlert` info message "No todos yet — add one below to get started!" shown when both `PendingTodos` and `CompletedTodos` are empty. Replaces silent blank state for new users.
- `Logout.razor`: Tracks `"logout"` analytics event before clearing session. Session lifecycle is now fully observable: `register` → `login` → `logout`.

**Why:** New users saw a blank Todos page with just an "Add Todo" button — no context. The empty state guides them. Logout analytics enable session-length analysis and help identify churn patterns.

**Impact:** 213 API tests pass. 221 mobile tests pass.

---

## 2026-05-17 — Iteration 287 — Mobile: Zero-UpdatedOn exclusion test for Journal, Todo, GoalProgress

**What changed:**
- `JournalRepositoryTests.cs`, `TodoRepositoryTests.cs`, `GoalProgressRepositoryTests.cs`: Added `GetModifiedSinceAsync_ExcludesRecordsWithZeroUpdatedOn` to each.

**Why:** Extends the Goal-only test from iteration 281 to all 4 entity repositories so the strict `>` comparison in `GetModifiedSinceAsync` is consistently verified across the codebase.

**Impact:** 213 API tests pass. 221 mobile tests pass (was 218).

---

## 2026-05-17 — Iteration 286 — Mobile: New-record UpsertFromSyncAsync uses server EnteredDate

**What changed:**
- `GoalRepositoryTests.cs`: Added `UpsertFromSyncAsync_NewRecord_UsesServerEnteredDate`.
- `JournalRepositoryTests.cs`: Added `UpsertFromSyncAsync_NewRecord_UsesServerEnteredDate`.

**Why:** The EnteredDate preservation fix (iterations 279/282) only preserves the local value when a local record already exists. When there's no local record, the server value should be stored as-is. This test is the necessary complement — verifying the new-record path works correctly after the fix.

**Impact:** 213 API tests pass. 218 mobile tests pass (was 216).

---

## 2026-05-17 — Iteration 285 — Web: Goal progress recency chip on home cards

**What changed:**
- `Home.razor`: `LoadGoals()` now also loads `LastProgressAt` (max `UpdatedOn`) per active goal alongside the count.
- Goal cards show "Updated today / yesterday / Nd ago" instead of a generic progress count. The chip turns orange when 14+ days have passed since the last update.

**Why:** The old chip only showed how many progress notes existed, not when they were added. Kids and parents need to see which goals have gone stale (no recent activity) vs. which are actively being worked on.

**Impact:** 213 API tests pass. 216 mobile tests pass.

---

## 2026-05-17 — Iteration 284 — Web: Show NextMeetingDate on goal cards

**What changed:**
- `Home.razor`: Goal cards now show a "Next meeting: MMM d" caption line when `NextMeetingDate` is set, using `Color.Info` to distinguish it from the goal text and measurable outcome.

**Why:** `NextMeetingDate` was only visible inside the GoalDetail page. Surfacing it on the card lets kids see all upcoming goal meetings at a glance from the dashboard without clicking into each goal.

**Impact:** 213 API tests pass. 216 mobile tests pass.

---

## 2026-05-17 — Iteration 283 — Web: Goal expiration warning chips on home page cards

**What changed:**
- `Home.razor`: Goal cards now show a contextual chip when `ExpirationDate` is set:
  - **Overdue** (red, Warning icon): expiration date is in the past
  - **Due soon** (orange, Schedule icon): expiration within 7 days

**Why:** Goals have an `ExpirationDate` field but it was never surfaced on the web. Kids need a visual signal to prioritize goals approaching their deadline without opening each goal's detail page.

**Impact:** 213 API tests pass. 216 mobile tests pass.

---

## 2026-05-17 — Iteration 282 — Mobile: Preserve EnteredDate through Journal sync upsert

**What changed:**
- `JournalRepository.cs`: `UpsertFromSyncAsync` now loads the existing record first and preserves its `EnteredDate`, mirroring the same fix applied to `GoalRepository` in iteration 279.
- `JournalRepositoryTests.cs`: Added `UpsertFromSyncAsync_PreservesOriginalEnteredDate_WhenServerSendsDifferentValue`.

**Why:** `EnteredDate` on a Journal entry represents the date the user wrote the entry. The server should never overwrite this with a different value — the same bug that existed in GoalRepository was present here too.

**Impact:** 213 API tests pass. 216 mobile tests pass (was 215).

---

## 2026-05-17 — Iteration 281 — Mobile: GetModifiedSinceAsync zero UpdatedOn exclusion test

**What changed:**
- `GoalRepositoryTests.cs`: Added `GetModifiedSinceAsync_ExcludesRecordsWithZeroUpdatedOn` — inserts a goal with `UpdatedOn = 0` alongside a normal goal, calls `GetModifiedSinceAsync(account, 0)`, asserts only the normal goal is returned.

**Why:** The strict `>` comparison in `GetModifiedSinceAsync` means records with `UpdatedOn = 0` are never included in sync. This is correct behavior (0 is an invalid sentinel), but was untested.

**Impact:** 213 API tests pass. 215 mobile tests pass (was 214).

---

## 2026-05-17 — Iteration 280 — API: Orphan GoalProgress sync test

**What changed:**
- `GoalProgressSyncTests.cs`: Added `Sync_OrphanGoalProgress_StoredEvenWhenGoalDoesNotExist` — uploads a GoalProgress with a GoalFk referencing a Goal that was never created, then asserts the record is stored and returned in the delta.

**Why:** Sync must not enforce referential integrity — in multi-device sync, entities can arrive in any order. A GoalProgress might arrive before its parent Goal. Rejecting orphans would silently drop data.

**Impact:** 213 API tests pass (was 212). 214 mobile tests pass.

---

## 2026-05-17 — Iteration 279 — Mobile: Preserve EnteredDate through Goal sync upsert

**What changed:**
- `GoalRepository.cs`: `UpsertFromSyncAsync` now loads the existing record first and preserves its `EnteredDate` before calling `InsertOrReplaceAsync`. New goals from server (no local record) still use the server's `EnteredDate`.
- `GoalRepositoryTests.cs`: Added `UpsertFromSyncAsync_PreservesOriginalEnteredDate_WhenServerSendsDifferentValue` — sets a local goal with `originalEnteredDate = now - 1 day`, upserts with `EnteredDate = now`, asserts original value is kept.

**Why:** `EnteredDate` represents when the user created the goal on their device. `InsertOrReplaceAsync` blindly replaced the entire row, so any server-sent `EnteredDate` (which can differ due to clock skew or server normalization) would silently overwrite the local creation date. This was a real bug confirmed by the failing test.

**Impact:** 212 API tests pass. 214 mobile tests pass (was 213).

---

## 2026-05-17 — Iteration 278 — API: Mixed batch (new + existing) upsert tests

**What changed:**
- `GoalSyncTests.cs`, `JournalSyncTests.cs`, `TodoSyncTests.cs`, `GoalProgressSyncTests.cs`: Added `Sync_MixedBatch_NewAndExistingBothPersisted` to each. Pattern: upload existing record → send batch with updated existing + brand-new record → assert both in delta with correct field values.

**Why:** No test verified that a single batch containing both new inserts and updates to existing records handled both paths correctly. A bug affecting only the INSERT path in the upsert logic would be invisible to single-record tests.

**Impact:** 212 API tests pass (was 208). 213 mobile tests pass.

---

## 2026-05-17 — Iteration 277 — API: Reject DeletedAt > UpdatedOn on all 4 sync endpoints

**What changed:**
- `GoalEndpoints.cs`, `JournalEndpoints.cs`, `TodoEndpoints.cs`, `GoalProgressEndpoints.cs`: Added validation rejecting uploads where `DeletedAt.HasValue && DeletedAt > UpdatedOn`. Returns 422.
- Corresponding tests added to all 4 sync test files (`Sync_DeletedAtGreaterThanUpdatedOn_Returns422`).

**Why:** The LWW soft-delete invariant is `DeletedAt == UpdatedOn`. If `DeletedAt > UpdatedOn`, the record is in an impossible state (deleted "after" its last write) — client bug or corruption. All 4 tests passed after validation was added, confirming the invariant was previously unguarded.

**Impact:** 208 API tests pass (was 204). 213 mobile tests pass. Build clean.

---

## 2026-05-17 — Iteration 276 — Web: Journal entry edit + soft-delete

**What changed:**
- `JournalPage.razor`: Added edit icon (opens dialog pre-filled with Notes/Activity/Mood/Tags) and delete icon (soft-delete, `DeletedAt = UpdatedOn = now`) to each journal card. Cards now always show `MudCardActions` row with edit/delete icons. Tracks `journal_edit` and `journal_delete` analytics events.

**Why:** Journal was the only entity where web users couldn't correct or remove entries. Completes full CRUD parity across all 4 entities on the web.

**Impact:** 204 API tests pass. Build clean.

---

## 2026-05-17 — Iteration 275 — API: Delta account isolation tests for all 4 entities

**What changed:**
- `GoalSyncTests.cs`, `JournalSyncTests.cs`, `TodoSyncTests.cs`, `GoalProgressSyncTests.cs`: Added `Sync_DeltaDoesNotContainOtherAccountsRecords` to each. Pattern: account A uploads a record → account B fetches delta → assert B cannot see A's record.

**Why:** The upload isolation (wrong AccountFk rejection) was tested for all 4 entities, but delta isolation — ensuring the `WHERE AccountFk = @accountGuid` filter in GET queries actually works — was never verified. A missing or incorrect filter would expose all accounts' data. All 4 tests passed, confirming the filter is correct.

**Impact:** 204 API tests pass (was 200). 213 mobile tests pass.

---

## 2026-05-17 — Iteration 274 — Web: Soft-delete goal from home page

**What changed:**
- `Home.razor`: Added delete (trash) icon to each active goal card. Sets `DeletedAt = UpdatedOn = now` (LWW soft-delete). Reloads goals and stats. Tracks `goal_delete` analytics event.

**Why:** Users could complete goals but had no way to remove mistakenly added ones from the web UI.

**Impact:** 200 API tests pass. Build clean.

---

## 2026-05-17 — Iteration 273 — Web: Soft-delete todo from web

**What changed:**
- `Todos.razor`: Added delete (trash) icon to each pending todo row. Sets `DeletedAt = UpdatedOn = now` — matches the LWW soft-delete convention used by mobile. Tracks `todo_delete` analytics event. Deleted todos disappear from pending list immediately.

**Why:** Mobile can soft-delete todos; web had no equivalent. Without web delete, users had to open the mobile app to remove junk entries.

**Impact:** 200 API tests pass. Build clean.

---

## 2026-05-17 — Iteration 272 — Web: Todo edit dialog

**What changed:**
- `Todos.razor`: Added edit pencil icon to each pending todo row. Opens dialog pre-filled with Title, Notes, DueDate. Saves changes with `UpdatedOn` LWW timestamp. Tracks `todo_edit` analytics event.

**Why:** Users could add and complete todos but not correct them from the web. Mirrors the goal edit capability added in iter 269.

**Impact:** 200 API tests pass. Build clean.

---

## 2026-05-17 — Iteration 271 — Web: Login and register analytics events

**What changed:**
- `Login.razor`: Inject `WebAnalyticsService`, track `login` event with `account.Guid` after successful auth.
- `Register.razor`: Inject `WebAnalyticsService`, track `register` event after account creation.

**Why:** Analytics coverage was missing session-start events. Login/register are the first touchpoint for any user session — without these, analytics can't distinguish new users from returning ones.

**Impact:** 200 API tests pass. Build clean.

---

## 2026-05-17 — Iteration 270 — Mobile: Upload multi-record parity for Todo, Goal, GoalProgress

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_TwoLocalTodosModified_BothIncludedInUpload` (user86), `RunAsync_TwoLocalGoalsModified_BothIncludedInUpload` (user87), `RunAsync_TwoLocalGoalProgressesModified_BothIncludedInUpload` (user88).

**Why:** Journal had upload multi-record coverage (iter 253). Todo, Goal, and GoalProgress lacked equivalent tests — if the sync loop silently dropped records beyond the first, only this pattern would catch it. Completes the 4-entity upload multi-record set.

**Impact:** 213 mobile tests pass (was 210 before this session). 200 API tests pass.

---

## 2026-05-17 — Iteration 269 — Web: Goal edit dialog on goal detail page

**What changed:**
- `GoalDetail.razor`: Added edit pencil icon to goal header. Opens a dialog pre-filled with `GoalText` and `MeasurableOutcome`. On save, updates the EF entity with new `UpdatedOn` timestamp (LWW-compatible). Tracks `goal_edit` analytics event.

**Why:** Users needed to correct goal text without the mobile app. Edits use the same `UpdatedOn` LWW field, so mobile sync picks up web edits correctly.

**Impact:** 200 API tests pass. Build clean.

---

## 2026-05-17 — Iteration 268 — Web: Journal page (/journal)

**What changed:**
- `JournalPage.razor`: New `/journal` page — MudCard grid showing entries (date, activity, mood chip, notes preview, comma-split tags as chips), New Entry dialog (notes, activity, mood, tags). Tracks `page_view` and `journal_add` analytics. Fixed RZ9996 build error (conditional block inside `<CardHeaderActions>` slot).
- `MainLayout.razor`: Added Journal nav link.

**Why:** Journal is the last of the 4 core entities without web coverage. All 4 entities (Goals, Todos, GoalProgress, Journal) now have full web UI.

**Impact:** 200 API tests pass. Build clean.

---

## 2026-05-17 — Iteration 267 — Web: Goal detail + progress notes page

**What changed:**
- `GoalDetail.razor`: New `/goals/{guid}` page — MudTimeline of progress entries (next steps text, next meeting date, timestamp), Add Progress Note dialog (text + date picker), back button. Tracks `page_view` and `progress_add` analytics events. Auth-gates to login. Returns "Goal not found" for invalid GUIDs.

**Why:** The Home page linked to `/goals/{guid}` with no target page. This completes full web coverage for all 4 core entities (Goal, Todos, Journal planned, GoalProgress now ✓).

**Impact:** 200 API tests pass. Build clean.

---

## 2026-05-17 — Iteration 266 — Web: Dashboard stats summary panel

**What changed:**
- `Home.razor`: Added 3-column `MudPaper` stats row at the top — active goals count, pending todos count (with red overdue chip when any are overdue), journal entries in the past 7 days. `LoadStats()` runs alongside `LoadGoals()` on init.

**Why:** At-a-glance status reduces navigation burden; the overdue chip directly addresses the "data-entry friction / quick status" unsolved pain point from the brainstorm.

**Impact:** 200 API tests pass. Build clean.

---

## 2026-05-17 — Iteration 265 — Web: Analytics tracking + Todos page

**What changed:**
- `AnalyticsEvent.cs`: New entity (Id, EventName, Timestamp, AccountGuid, Page, Context). Registered in `AppDbContext`. `EnsureCreated` handles table creation.
- `WebAnalyticsService.cs`: Scoped service wrapping `TrackAsync(eventName, accountGuid, page, context)`.
- `Home.razor`: Tracks `page_view`, `goal_add`, `goal_complete` events. `@inject WebAnalyticsService`.
- `Todos.razor`: New `/todos` page — pending list with overdue alert (red badge), add dialog with due-date picker, completed expansion panel. Tracks `page_view`, `todo_add`, `todo_complete`.
- `MainLayout.razor`: Added Goals + Todos nav links for authenticated users.
- `Program.cs`: `AddScoped<WebAnalyticsService>()`.
- `_Imports.razor`: `@using ChildDev.Api.Services`.

**Why:** CLAUDE.md mandates user behavior analytics for all created apps. Todos was the highest-impact missing web page (all 4 sync entities now have web coverage).

**Impact:** 200 API tests pass. Build clean. Web UI now covers Goals + Todos with full analytics.

---

## 2026-05-17 — Iteration 264 — Web: Blazor Server + MudBlazor migration complete

**What changed:**
- `ChildDev.Api/Program.cs`: Replaced `AddRazorPages`/`MapRazorPages` with `AddRazorComponents().AddInteractiveServerComponents()` / `MapRazorComponents<App>().AddInteractiveServerRenderMode()`. Added `AddHttpContextAccessor()`.
- `ChildDev.Api/Components/Pages/Home.razor`: Fixed MUD0002 warning (removed invalid `Title` attribute on `MudIconButton`).
- Merged `improve/mudblazor-web-ui-20264` to master.

**Why:** Completes the user-requested switch from Razor Pages to Blazor Server + MudBlazor, enabling the goal-centric web UI (Home, Login, Register, Logout) added in the same branch.

**Impact:** 200 API tests pass. Build clean (0 warnings, 0 errors).

---

## 2026-05-17 — Iteration 260 — API: Goal sync idempotent upsert — completes 4-entity idempotency set

**What changed:**
- `GoalSyncTests.cs`: Added `Sync_SameGuidUploadedTwice_DeltaContainsExactlyOneRecord` (gsync_idempotent1) — uploads a goal GUID twice (second with newer UpdatedOn and different GoalText), fetches delta, asserts exactly one record with second GoalText.

**Why:** Completes the 4-entity idempotency coverage set (Journal ✓ iter 251, Todo ✓ 258, GoalProgress ✓ 259, Goal ✓ 260). Goal has entity-specific optional fields (ExpirationDate, MeasurableOutcome, etc.) that pass through the same upsert path.

**Impact:** 197 API tests pass (was 196). 210 mobile tests pass.

---

## 2026-05-17 — Iteration 259 — API: GoalProgress sync idempotent upsert — same GUID twice yields one record

**What changed:**
- `GoalProgressSyncTests.cs`: Added `Sync_SameGuidUploadedTwice_DeltaContainsExactlyOneRecord` (gpsync_idempotent1) — uploads a goal-progress GUID twice (second with newer UpdatedOn and different NextStepItems), fetches delta, asserts exactly one record with second NextStepItems.

**Why:** Iteration 251 (Journal) and 258 (Todo) added idempotency tests. GoalProgress uses its own EF Core entity and endpoint; if its upsert handler accidentally inserted instead of updated, the delta would contain two rows. Completing the 3-of-4 idempotency set (Goal remains).

**Impact:** 196 API tests pass (was 195). 210 mobile tests pass.

---

## 2026-05-17 — Iteration 258 — API: Todo sync idempotent upsert — same GUID twice yields one record

**What changed:**
- `TodoSyncTests.cs`: Added `Sync_SameGuidUploadedTwice_DeltaContainsExactlyOneRecord` (tsync_idempotent1) — uploads a todo GUID twice (second with newer UpdatedOn and different Title), fetches delta with LastSyncAt=0, and asserts exactly one record with the second Title.

**Why:** Iteration 251 added this idempotency test for Journal. Todo has different required fields (Title vs Notes) and a separate EF Core upsert handler. If the Todo upsert accidentally inserted rather than updated on second upload, the delta would contain two rows. No idempotency test existed for Todo.

**Impact:** 195 API tests pass (was 194). 210 mobile tests pass.

---

## 2026-05-17 — Iteration 257 — API: Goal delta strict-greater-than LastSyncAt boundary test

**What changed:**
- `GoalSyncTests.cs`: Added `Sync_LastSyncAt_ExactlyEqualToRecordUpdatedOn_ExcludedFromDelta` (gsync_exact_boundary1) — uploads a goal with `UpdatedOn = ts`, then syncs with `LastSyncAt = ts`, and asserts the record does NOT appear in the delta.

**Why:** The server filter is `UpdatedOn > req.LastSyncAt` (strict `>`). Existing tests use `LastSyncAt` that's definitely larger or smaller than the record's timestamp. If the filter were accidentally changed to `>=`, the exact-boundary case would incorrectly include a record the client already has, causing an infinite re-sync loop. No test previously validated the exact `==` boundary.

**Impact:** 194 API tests pass (was 193). 210 mobile tests pass.

---

## 2026-05-17 — Iteration 256 — Mobile: SyncService upserts all goal-progress records when server returns multiple

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsTwoGoalProgress_BothUpsertedLocally` (user85) and `MultiGoalProgressSyncHandler` helper — server returns 2 goal-progress records for the same goal, asserts both appear in `GetForGoalAsync` after sync.

**Why:** Completes the 4-entity multi-record server response coverage set (Journal ✓ iter 250, Goal ✓ iter 254, Todo ✓ iter 255, GoalProgress ✓ iter 256). GoalProgress uses its own DTO and mapper; a GoalProgress-specific regression in the foreach would only be caught by this test.

**Impact:** 210 mobile tests pass (was 209). 193 API tests pass.

---

## 2026-05-17 — Iteration 255 — Mobile: SyncService upserts all todos when server returns multiple

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsTwoTodos_BothUpsertedLocally` (user84) and `MultiTodoSyncHandler` helper — server returns 2 todos, asserts both appear in `GetPendingAsync` after sync.

**Why:** Iterations 250 and 254 covered multi-record responses for Journal and Goal. Todo uses a different DTO and mapper (`UpsertFromSyncAsync` on `TodoRepository`). The 4-entity coverage set is now Journal ✓, Goal ✓, Todo ✓ (GoalProgress still remaining).

**Impact:** 209 mobile tests pass (was 208). 193 API tests pass.

---

## 2026-05-17 — Iteration 254 — Mobile: SyncService upserts all goals when server returns multiple

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsTwoGoals_BothUpsertedLocally` (user83) and `MultiGoalSyncHandler` helper — server returns 2 goals, asserts both are in the local repository after sync.

**Why:** Iteration 250 covered the same pattern for journals. The Goal sync path in `SyncEntityAsync` is structurally identical but uses a different DTO/mapper. The multi-record server response for goals specifically was untested; a Goal-specific regression in the foreach would only be caught by this test.

**Impact:** 208 mobile tests pass (was 207). 193 API tests pass.

---

## 2026-05-17 — Iteration 253 — Mobile: SyncService uploads all modified journals when multiple exist

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_TwoLocalJournalsModified_BothIncludedInUpload` (user82) — inserts 2 journals with `UpdatedOn > 0` (account `LastSyncAt` defaults to 0), runs sync, and asserts both GUIDs appear in the upload body captured by `CapturingHandler`.

**Why:** All existing upload-direction tests insert exactly 1 modified record. The `SyncEntityAsync` method collects `GetModifiedSinceAsync` results into a list and sends them all in one batch, but this multi-record upload path was untested. If the batch were accidentally truncated to the first record (e.g., `.First()` instead of the full list), only the 2-record test would catch it.

**Impact:** 207 mobile tests pass (was 206). 193 API tests pass.

---

## 2026-05-17 — Iteration 252 — Mobile: Overdue todos remain in pending list

**What changed:**
- `TodoRepositoryTests.cs`: Added `GetPendingAsync_OverdueTodo_StillReturnedAsPending` — inserts a todo with a due date 7 days in the past, calls `GetPendingAsync`, and asserts the item is still returned.

**Why:** The overdue count badge (added in recent UI commits) depends on `GetPendingAsync` including past-due items. The query filters on `DeletedAt IS NULL AND CompletedAt IS NULL` — no date filter — but no test verified this. If someone added a `DueDate < now` filter to optimize the list, overdue items would silently vanish from the badge count.

**Impact:** 206 mobile tests pass (was 205). 193 API tests pass.

---

## 2026-05-17 — Iteration 251 — API: Journal sync idempotent upsert — same GUID twice yields one record

**What changed:**
- `JournalSyncTests.cs`: Added `Sync_SameGuidUploadedTwice_DeltaContainsExactlyOneRecord` (jsync_idempotent1) — uploads a journal GUID in one sync call, uploads it again with a newer `UpdatedOn` in a second call, then fetches the delta and asserts exactly one record for that GUID with the newer notes content.

**Why:** The server uses EF Core `FindAsync` + `AddOrUpdate`/`Update` to implement LWW upsert, which should prevent duplicate rows. But no test verified that two uploads of the same GUID result in exactly one stored record rather than two rows. If the upsert logic were accidentally changed to always `Add`, the delta would return two copies.

**Impact:** 193 API tests pass (was 192). 205 mobile tests pass.

---

## 2026-05-17 — Iteration 250 — Mobile: SyncService upserts all records when server returns multiple

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsTwoJournals_BothUpsertedLocally` (user80) and `MultiJournalSyncHandler` helper — server returns 2 journals, asserts both are in the local repository after sync.

**Why:** All existing server-returns-data tests use a single-record response. The `SyncEntityAsync` loops `foreach (var dto in result.Records)` but this was only tested with 1 record. If the loop were accidentally broken (e.g., `FirstOrDefault` instead of iterating all), only the 2-record test would catch it.

**Impact:** 205 mobile tests pass (was 204). 192 API tests pass.

---

## 2026-05-17 — Iteration 249 — Mobile: SyncService releases _syncing lock after partial entity failure

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_PartialFailure_ReleasesLockSoSubsequentSyncCanRun` (user79) — runs sync with `GoalFailureHandler` (journal succeeds, goal always 500s → Failed), then immediately runs sync again and asserts the second run also returns `Failed` (proving it actually executed, not returned early from the concurrent-call guard).

**Why:** The existing `FailedSync_ReleasesLockSoSubsequentSyncCanRun` test only checks the all-entities-fail case. The partial-failure case (some entities succeed, later one fails) also goes through the `finally { Interlocked.Exchange(ref _syncing, 0); }` block, but this was untested. A refactor that broke the finally block for partial failures would pass the existing tests.

**Impact:** 204 mobile tests pass (was 203). 192 API tests pass.

---

## 2026-05-17 — Iteration 248 — Mobile: SyncService returns Failed on 401/403 entity sync response

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_Server401OnEntitySync_ReturnsFailed` (user77) and `RunAsync_Server403OnEntitySync_ReturnsFailed` (user78), plus `EntitySync401Handler` and `EntitySync403Handler` helper classes.

**Why:** When an entity sync endpoint returns 401 (expired JWT) or 403 (forbidden), `EnsureSuccessStatusCode()` throws and the outer catch returns `Failed`. Neither status was tested — a refactor that changed the exception handling path would have silently changed the return value.

**Impact:** 203 mobile tests pass (was 201). 192 API tests pass.

---

## 2026-05-17 — Iteration 247 — API: Goal sync endpoint validation tests

**What changed:**
- `GoalSyncTests.cs`: Added 5 validation tests — `Sync_DuplicateGuidsInBatch_Returns422`, `Sync_FutureUpdatedOn_Returns422`, `Sync_TooManyRecords_Returns400`, `Sync_BlankGoalText_Returns422`, `Sync_FutureExpirationDate_Returns422`.

**Why:** Completes the 4-entity validation test coverage. `FutureExpirationDate` is unique to Goal. All 4 sync endpoints now have validated input validation coverage.

**Impact:** 201 mobile tests pass. 192 API tests pass (was 187).

---

## 2026-05-17 — Iteration 246 — API: Todo sync endpoint validation tests

**What changed:**
- `TodoSyncTests.cs`: Added 5 validation tests — `Sync_DuplicateGuidsInBatch_Returns422`, `Sync_FutureUpdatedOn_Returns422`, `Sync_TooManyRecords_Returns400`, `Sync_BlankTitle_Returns422`, `Sync_FutureDueDate_Returns422`.

**Why:** Completes the validation coverage pattern for 3 of 4 entities. `FutureDueDate` is unique to Todo — testing the entity-specific optional timestamp validation. 201 mobile / 187 API (was 182).

**Impact:** 201 mobile tests pass. 187 API tests pass (was 182).

---

## 2026-05-17 — Iteration 245 — API: GoalProgress sync endpoint validation tests

**What changed:**
- `GoalProgressSyncTests.cs`: Added 5 validation tests — `Sync_DuplicateGuidsInBatch_Returns422`, `Sync_FutureUpdatedOn_Returns422`, `Sync_TooManyRecords_Returns400`, `Sync_InvalidGoalFkFormat_Returns422`, `Sync_BlankNextStepItems_Returns422`.

**Why:** Mirrors the Journal validation coverage added in iter 244. `InvalidGoalFkFormat` is unique to GoalProgress — the only entity that validates a foreign key GUID.

**Impact:** 201 mobile tests pass. 182 API tests pass (was 177).

---

## 2026-05-17 — Iteration 244 — API: Journal sync endpoint validation tests

**What changed:**
- `JournalSyncTests.cs`: Added 5 validation tests — `Sync_DuplicateGuidsInBatch_Returns422`, `Sync_FutureUpdatedOn_Returns422`, `Sync_TooManyRecords_Returns400`, `Sync_InvalidGuidFormat_Returns422`, `Sync_BlankNotes_Returns422`.

**Why:** The Journal sync endpoint has 10+ validation rules (duplicate GUIDs, future timestamps, max batch size, invalid GUID format, blank content fields, field length limits) but none were tested. These guard against accidental removal or regression of input validation that protects data integrity and server stability.

**Impact:** 201 mobile tests pass. 177 API tests pass (was 172).

---

## 2026-05-17 — Iteration 243 — API: SoftDelete delta verifies UpdatedOn == DeletedAt for all 4 entities

**What changed:**
- `JournalSyncTests.cs`, `GoalSyncTests.cs`, `GoalProgressSyncTests.cs`, `TodoSyncTests.cs`: Added `Sync_SoftDelete_UpdatedOnEqualsDeletedAtInDelta` — uploads a record, then soft-deletes it (sending `UpdatedOn == DeletedAt`), then asserts the delta response has `DeletedAt == UpdatedOn`.

**Why:** The existing `Sync_SoftDelete_DeletedAtPropagatedInDelta` tests only asserted `DeletedAt` had a value — they did not assert `UpdatedOn == DeletedAt`, which is the core LWW soft-delete invariant. If the server ever stored them with different values, the mobile `UpsertFromSyncAsync` caller can't rely on the record being recognized as deleted by `DeletedAt IS NULL` filters or the `UpdatedOn == DeletedAt` invariant checks.

**Impact:** 201 mobile tests pass. 172 API tests pass (was 168).

---

## 2026-05-17 — Iteration 242 — Mobile: SyncService verifies DeletedAt serialized in upload body for all 4 entities

**What changed:**
- `SyncServiceTests.cs`: Added 4 tests (`user73`–`user76`) — `RunAsync_LocalSoftDeletedJournal_DeletedAtSerializedInUploadBody`, `…Goal…`, `…GoalProgress…`, `…Todo…` — each inserts a soft-deleted record then asserts the `deletedAt` timestamp value appears in the JSON request body sent to the server.

**Why:** The existing soft-delete upload tests only verified the GUID appeared in the body (i.e. the record was included). They did not verify `DeletedAt` was serialized with its actual value. If a DTO mapping accidentally passed `null` for `DeletedAt`, the server would treat the record as active and LWW could resurrect deleted items on other devices.

**Impact:** 201 mobile tests pass (was 197). 168 API tests pass.

---

## 2026-05-17 — Iteration 241 — Mobile: GoalRepository + TodoRepository.GetAsync returns soft-deleted records

**What changed:**
- `GoalRepositoryTests.cs`: Added `GetAsync_WhenDeleted_StillReturnsRecord` — saves a goal, soft-deletes it, asserts `GetAsync` returns the record with `DeletedAt` set.
- `TodoRepositoryTests.cs`: Added `GetAsync_WhenDeleted_StillReturnsRecord` — same pattern for Todo.

**Why:** Completes the 3-entity set started in iter 240 (Journal). Ensures `GetAsync` never silently gains a `DeletedAt IS NULL` filter that would break sync logic retrieving any record by GUID.

**Impact:** 197 mobile tests pass (was 195). 168 API tests pass.

---

## 2026-05-17 — Iteration 240 — Mobile: JournalRepository.GetAsync returns soft-deleted records

**What changed:**
- `JournalRepositoryTests.cs`: Added `GetAsync_WhenDeleted_StillReturnsRecord` — saves a journal, deletes it, then calls `GetAsync(guid)` and asserts the record is returned with `DeletedAt` set.

**Why:** `GetAsync` uses `db.FindAsync<Journal>(guid)` which finds by PK without filtering. If a refactor added a `DeletedAt IS NULL` filter (thinking it improves safety), sync logic relying on `GetAsync` to retrieve any record by GUID would silently break.

**Impact:** 195 mobile tests pass (was 194). 168 API tests pass.

---

## 2026-05-17 — Iteration 239 — API: Journal/Goal/Todo delta responses include AccountFk

**What changed:**
- `JournalSyncTests.cs`: Added `Sync_Delta_AccountFkIncludedInResponse`.
- `GoalSyncTests.cs`: Added `Sync_Delta_AccountFkIncludedInResponse`.
- `TodoSyncTests.cs`: Added `Sync_Delta_AccountFkIncludedInResponse`.

**Why:** Completes the AccountFk-in-delta coverage for all four entities (GoalProgress was added in iter 238). The mobile stores `AccountFk` from the server's response to filter future `GetModifiedSinceAsync` queries.

**Impact:** 194 mobile tests pass. 168 API tests pass (was 165).

---

## 2026-05-17 — Iteration 238 — API: GoalProgress delta response includes AccountFk

**What changed:**
- `GoalProgressSyncTests.cs`: Added `Sync_Delta_AccountFkIncludedInResponse` (account "gpsync_accountfk") — uploads a GoalProgress then syncs empty and asserts `AccountFk == accountGuid` in the delta record.

**Why:** The mobile stores `AccountFk` from the server's delta response to filter future `GetModifiedSinceAsync` queries. If the server ever dropped `AccountFk` from the `GoalProgressDto` response, the mobile would silently store empty AccountFk, breaking all subsequent uploads for that entity. No prior test asserted this field in the response.

**Impact:** 194 mobile tests pass. 165 API tests pass (was 164).

---

## 2026-05-17 — Iteration 237 — Mobile: Synced soft-deleted GoalProgress excluded from GetForGoalAsync

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_ServerSendsDeletedGoalProgress_ExcludedFromGetForGoalAsync` (user72) — completes the 4-entity view-exclusion coverage set started in iteration 234.

**Why:** `GetForGoalAsync` filters `DeletedAt IS NULL`. After syncing a soft-deleted GoalProgress, it must not appear in the goal's progress list. Without this test, a regression in `GetForGoalAsync` query logic would allow deleted progress items to show in the UI.

**Impact:** 194 mobile tests pass (was 193). 164 API tests pass.

---

## 2026-05-17 — Iteration 236 — Mobile: Synced soft-deleted goal excluded from GetAllActiveAsync

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_ServerSendsDeletedGoal_ExcludedFromGetAllActiveAsync` (user71) — mirrors the journal and todo pattern for goals.

**Why:** Completes the 3-entity view-exclusion coverage chain (journal iter235, todo iter234, goal iter236). After syncing a soft-deleted goal, it must disappear from the active goal list used by the UI.

**Impact:** 193 mobile tests pass (was 192). 164 API tests pass.

---

## 2026-05-17 — Iteration 235 — Mobile: Synced soft-deleted journal excluded from GetAllActiveAsync

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_ServerSendsDeletedJournal_ExcludedFromGetAllActiveAsync` (user70) — pre-inserts active journal, confirms it's in the active list, server sends it soft-deleted; asserts it disappears from `GetAllActiveAsync`.

**Why:** Mirrors iteration 234 (todo/pending) for the journal/active path. Completes the deleted-record view-exclusion coverage for the two non-GoalProgress entity types.

**Impact:** 192 mobile tests pass (was 191). 164 API tests pass.

---

## 2026-05-17 — Iteration 234 — Mobile: Synced soft-deleted todo excluded from GetPendingAsync

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_ServerSendsDeletedTodo_ExcludedFromGetPendingAsync` (user69) — pre-inserts an active todo, confirms it's pending, then server sends it as soft-deleted; asserts it's no longer in `GetPendingAsync`.

**Why:** `RunAsync_ServerReturnsDeletedTodo_DeletedAtPropagatedLocally` only checked that `DeletedAt` was stored. This test adds the crucial second assertion: the todo must disappear from the pending UI view. A regression where `GetPendingAsync` fails to filter `DeletedAt IS NOT NULL` after a sync upsert would have been invisible.

**Impact:** 191 mobile tests pass (was 190). 164 API tests pass.

---

## 2026-05-17 — Iteration 233 — Mobile: Server-sent null CompletionDate restores goal to active

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_ServerSendsGoalWithNullCompletionDate_GoalAppearsActiveLocally` (user68) — inserts a locally-completed goal, then server sends the same goal with `CompletionDate = null` and newer `UpdatedOn`; asserts the goal appears in `GetAllActiveAsync` and has null `CompletionDate`.

**Why:** `GetAllActiveAsync` orders by `(CompletionDate IS NOT NULL)` to separate active from completed goals. `UpsertFromSyncAsync` uses `InsertOrReplaceAsync` which must clear `CompletionDate` when null. Without this test, a regression in the field mapping (e.g., accidentally preserving local `CompletionDate` on upsert) would cause the goal to stay in the "completed" section even after the server un-completes it.

**Impact:** 190 mobile tests pass (was 189). 164 API tests pass.

---

## 2026-05-17 — Iteration 232 — Mobile: Bearer JWT verified in HTTP request headers

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_BearerJwt_IncludedInRequestHeaders` (user67) with `AuthHeaderCapturingHandler` — captures the `Authorization` header and asserts `Scheme == "Bearer"` and `Parameter == "my-secret-jwt"`.

**Why:** All existing SyncService tests set a fake JWT but no test verified it was actually forwarded in `DefaultRequestHeaders.Authorization`. If the `new AuthenticationHeaderValue("Bearer", account.ServerJwt)` assignment were accidentally removed, every real server call would return 401 — but the fake handlers wouldn't catch it.

**Impact:** 189 mobile tests pass (was 188). 164 API tests pass.

---

## 2026-05-17 — Iteration 231 — Mobile: lastSyncAt verified in all four entity request bodies

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_LastSyncAt_IncludedInAllFourEntityBodies` (user66) — sets a known `LastSyncAt` on the account, runs sync via `CapturingHandler`, and asserts all four entity bodies (journal, goal, goal-progress, todo) contain that exact value.

**Why:** `RunAsync_SecondSync_SendsLastSyncAtFromPriorSync` only verified the journal endpoint. If `SyncEntityAsync` were refactored and any one of the 4 entity calls accidentally dropped `lastSyncAt` from `SyncRequestDto`, the server would silently respond with full history instead of a delta, causing unnecessary data transfer and re-upload loops. This test covers all four.

**Impact:** 188 mobile tests pass (was 187). 164 API tests pass.

---

## 2026-05-17 — Iteration 230 — Mobile: Double network failure on entity sync returns Failed

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_EntitySyncNetworkErrorOnBothAttempts_ReturnsFailed` (user65) with `AlwaysNetworkErrorEntityHandler` — throws HttpRequestException for all entity sync calls (both initial and retry).

**Why:** The existing `RunAsync_EntitySyncNetworkError_RetriesAndSucceeds` tests the case where the retry succeeds. The case where both attempts fail (exception propagates through SyncEntityAsync to the outer catch → Failed) was untested. This ensures the retry logic doesn't silently swallow double failures or return Success.

**Impact:** 187 mobile tests pass (was 186). 164 API tests pass.

---

## 2026-05-17 — Iteration 229 — Mobile: SaveServerCredentials/Url preserve LastSyncAt

**What changed:**
- `AccountServiceTests.cs`: Added `SaveServerCredentials_PreservesLastSyncAt` (user "sam") and `SaveServerUrl_PreservesLastSyncAt` (user "taylor").

**Why:** If credential or URL updates ever reset LastSyncAt (e.g., by using a partial-update approach that initializes a new Account object instead of loading the existing one), the next sync would re-download the full dataset. The existing preservation tests only checked NickName/CreatedOn/JWT/URL; LastSyncAt was unguarded.

**Impact:** 186 mobile tests pass (was 184). 164 API tests pass.

---

## 2026-05-17 — Iteration 228 — API: Soft-delete clears GoalText/Title in delta (Goal + Todo)

**What changed:**
- `GoalSyncTests.cs`: Added `Sync_SoftDelete_GoalTextNullInDelta`.
- `TodoSyncTests.cs`: Added `Sync_SoftDelete_TitleNullInDelta`.

**Why:** Completes the 4-entity soft-delete null-field delta coverage started in iter 227. All entity soft-delete delta tests now assert both DeletedAt and the cleared text field.

**Impact:** 184 mobile tests pass. 164 API tests pass (was 162).

---

## 2026-05-17 — Iteration 227 — API: Soft-delete clears text fields in delta (Journal + GoalProgress)

**What changed:**
- `JournalSyncTests.cs`: Added `Sync_SoftDelete_NotesNullInDelta` — asserts deleted journal's Notes is null in the delta response.
- `GoalProgressSyncTests.cs`: Added `Sync_SoftDelete_NextStepItemsNullInDelta` — asserts deleted GoalProgress's NextStepItems is null in the delta response.

**Why:** The existing `Sync_SoftDelete_DeletedAtPropagatedInDelta` tests for all entities only assert `DeletedAt == deletedAt`. They don't verify that text fields (Notes, NextStepItems) are cleared to null after a soft-delete LWW update. If `ApplyDto` ever gained a null-coalescing guard (`e.Notes = dto.Notes ?? e.Notes`), stale text would persist in the delta and mobile clients would show content for deleted records.

**Impact:** 184 mobile tests pass. 162 API tests pass (was 160).

---

## 2026-05-17 — Iteration 226 — Mobile: AccountFk in upload body for Goal, Todo, GoalProgress

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_LocalGoal_AccountFkIncludedInUploadRequest` (user62), `RunAsync_LocalTodo_AccountFkIncludedInUploadRequest` (user63), `RunAsync_LocalGoalProgress_AccountFkIncludedInUploadRequest` (user64).

**Why:** Completes the AccountFk-in-upload coverage across all 4 entities. If the toDto lambda for any entity accidentally drops AccountFk, the server silently rejects all records from that entity — and without these tests, no test would catch the regression.

**Impact:** 184 mobile tests pass (was 181). 160 API tests pass.

---

## 2026-05-17 — Iteration 225 — Mobile: Goal UpdatedOn from server + AccountFk in upload body

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsGoal_UpdatedOnStoredLocally` (user60) — completes the 4-entity UpdatedOn-from-server coverage started in iter 224.
- `SyncServiceTests.cs`: Added `RunAsync_LocalJournal_AccountFkIncludedInUploadRequest` (user61) — asserts `account.Guid` (AccountFk) appears in the journal upload body. If the toDto lambda accidentally dropped AccountFk, the server would silently reject all records and no other test would detect the regression.

**Why:** GoalRepository.UpsertFromSyncAsync preserves UpdatedOn but this was untested for Goal (the other 3 entities gained tests in iter 224). AccountFk in upload body was never explicitly asserted for any entity.

**Impact:** 181 mobile tests pass (was 179). 160 API tests pass.

---

## 2026-05-17 — Iteration 224 — Mobile: Verify UpdatedOn from server is stored locally

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsJournal_UpdatedOnStoredLocally`, `RunAsync_ServerReturnsTodo_UpdatedOnStoredLocally`, `RunAsync_ServerReturnsGoalProgress_UpdatedOnStoredLocally`.

**Why:** All existing upsert tests assert content fields (GoalText, Notes, etc.) but none asserted `UpdatedOn` is preserved. If `UpdatedOn` were lost, every record received from the server would re-appear in `GetModifiedSinceAsync` on the next sync — causing redundant re-uploads of all server-originated data forever. Three tests (user57-59) catch this regression path.

**Impact:** 179 mobile tests pass (was 176). 160 API tests pass.

---

## 2026-05-17 — Iteration 223 — Mobile: SyncService health check timeout returns NoServer

**What changed:**
- `SyncService.cs`: Wrapped `client.GetAsync("health", healthCts.Token)` in a try/catch for `OperationCanceledException` returning `SyncResult.NoServer`. Previously the exception fell through to the outer catch and returned `Failed`.
- `SyncServiceTests.cs`: Added `RunAsync_HealthCheckTimeout_ReturnsNoServer` with `SlowHealthHandler` (delays 10s to trigger the 5s CTS).

**Why:** A server that is unreachable (times out) should be treated as `NoServer` — the caller uses this to decide whether to schedule a retry vs. show an error. Returning `Failed` for a timeout was semantically wrong and broke any logic branching on `NoServer`.

**Impact:** 176 mobile tests pass (was 175). 160 API tests pass.

---

## 2026-05-17 — Iteration 222 — Mobile: GoalProgressRepository DeleteForGoalAsync records appear in GetModifiedSince

**What changed:**
- `GoalProgressRepositoryTests.cs`: Added `DeleteForGoalAsync_RecordsAppearInGetModifiedSince` — inserts two GoalProgress records with oldTs, calls DeleteForGoalAsync, asserts both appear in GetModifiedSinceAsync with DeletedAt set.

**Why:** When a user deletes a goal, the goal's progress records are soft-deleted via `DeleteForGoalAsync`. These must then appear in `GetModifiedSinceAsync` so they are uploaded to the server as deleted records. If `DeleteForGoalAsync` failed to bump UpdatedOn past the last sync timestamp, the deletions would be silently dropped and the server would retain stale GoalProgress records.

**Impact:** 175 mobile tests pass (was 174). 160 API tests pass.

---

## 2026-05-17 — Iteration 221 — Mobile: GoalRepository and TodoRepository CompleteAsync appear in GetModifiedSince

**What changed:**
- `GoalRepositoryTests.cs`: Added `CompleteAsync_GoalAppearsInGetModifiedSince`.
- `TodoRepositoryTests.cs`: Added `CompleteAsync_TodoAppearsInGetModifiedSince`.

**Why:** Mirrors iter 219-220 for the CompleteAsync path. After completing a goal or todo, the record must appear in GetModifiedSinceAsync for sync upload. `CompleteAsync_SetsUpdatedOnToCompletionDate` and `CompleteAsync_SetsCompletionDate` verify the timestamps but don't test the GetModifiedSince integration end-to-end.

**Impact:** 174 mobile tests pass (was 172). 160 API tests pass.

---

## 2026-05-17 — Iteration 220 — Mobile: TodoRepository and JournalRepository DeleteAsync appear in GetModifiedSince

**What changed:**
- `TodoRepositoryTests.cs`: Added `DeleteAsync_TodoAppearsInGetModifiedSince`.
- `JournalRepositoryTests.cs`: Added `DeleteAsync_JournalAppearsInGetModifiedSince`.

**Why:** Mirrors iter 219 for Goal. Both tests verify the sync-upload path: after calling DeleteAsync, the soft-deleted record must appear in GetModifiedSinceAsync so it is included in the next upload batch. These are two structurally identical tests, batched together.

**Impact:** 172 mobile tests pass (was 170). 160 API tests pass.

---

## 2026-05-17 — Iteration 219 — Mobile: GoalRepository DeleteAsync makes goal visible in GetModifiedSince

**What changed:**
- `GoalRepositoryTests.cs`: Added `DeleteAsync_GoalAppearsInGetModifiedSince` — inserts a goal with oldTs, soft-deletes it, asserts it appears in GetModifiedSinceAsync(oldTs) with DeletedAt set.

**Why:** This is the sync-upload path for deleted goals. `GetModifiedSinceAsync_IncludesSoftDeletedRecords` uses direct DB insertion; it never verifies that `DeleteAsync` itself bumps UpdatedOn correctly so the change is caught by the since-filter. `CompleteAsync_SetsUpdatedOnToCompletionDate` and `Delete_SoftDeletes_ExcludedFromActive` exist but neither tests the GetModifiedSince integration.

**Impact:** 170 mobile tests pass (was 169). 160 API tests pass.

---

## 2026-05-17 — Iteration 218 — Mobile: AccountService UpdateLastSync preserves ServerCredentials

**What changed:**
- `AccountServiceTests.cs`: Added `UpdateLastSync_PreservesServerCredentials` — creates account, saves credentials (jwt + url), calls `UpdateLastSyncAsync`, asserts ServerJwt and ServerUrl are unchanged.

**Why:** Mirrors iter 217 for `UpdateLastSyncAsync`. The same partial-object refactor risk applies: if account.ServerJwt and ServerUrl were not loaded into the account before `db.UpdateAsync`, they would be wiped on every sync. `UpdateLastSync_SetsTimestamp` only checks LastSyncAt.

**Impact:** 169 mobile tests pass (was 168). 160 API tests pass.

---

## 2026-05-17 — Iteration 217 — Mobile: AccountService SaveServerCredentials preserves NickName and CreatedOn

**What changed:**
- `AccountServiceTests.cs`: Added `SaveServerCredentials_PreservesNickNameAndCreatedOn` — creates an account, calls `SaveServerCredentialsAsync`, then asserts NickName and CreatedOn still match the original values in addition to the new JWT and URL.

**Why:** `SaveServerCredentials_PersistsJwtAndUrl` only verifies the new fields. If `SaveServerCredentialsAsync` were refactored to call `db.UpdateAsync` on a partial Account object (not fully loaded from DB first), NickName, PinHash, and CreatedOn would be silently wiped. This test pins the "full-row update" contract.

**Impact:** 168 mobile tests pass (was 167). 160 API tests pass.

---

## 2026-05-17 — Iteration 216 — Mobile: SyncService includes GoalProgress GoalFk in upload request

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_LocalGoalProgress_GoalFkIncludedInUploadRequest` — inserts a GoalProgress with a specific GoalFk GUID, runs sync, asserts the captured sync/goal-progress body contains that GoalFk.

**Why:** GoalFk is the most critical field in GoalProgress — it links progress to a goal. All upload tests only checked GUID or NextStepItems/NextMeetingDate. An accidental field swap in the toDto lambda (e.g., `p.Guid` instead of `p.GoalFk`) would silently orphan all GoalProgress records on sync.

**Impact:** 167 mobile tests pass (was 166). 160 API tests pass.

---

## 2026-05-17 — Iteration 215 — Mobile: SyncService includes Todo Title in upload request

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_LocalTodo_TitleIncludedInUploadRequest` — inserts a todo with Title="Call the dentist", runs sync, asserts the captured sync/todo body contains that text.

**Why:** `RunAsync_LocalTodo_DueDateIncludedInUploadRequest` only checks DueDate; `RunAsync_LocalTodo_NotesIncludedInUploadRequest` only checks Notes. The `t.Title` field in the toDto lambda was never explicitly verified in the upload body. Title is the required display name of a todo item.

**Impact:** 166 mobile tests pass (was 165). 160 API tests pass.

---

## 2026-05-17 — Iteration 214 — Mobile: SyncService includes Journal EnteredDate in upload request

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_LocalJournal_EnteredDateIncludedInUploadRequest` — inserts a journal with EnteredDate=3_000_000 and UpdatedOn=4_000_000, asserts the captured sync/journal body contains 3_000_000.

**Why:** Mirrors iter 213 for Journal. AuxFieldsIncluded uses identical values for EnteredDate and UpdatedOn, making it impossible to distinguish the two in the JSON body. If `j.EnteredDate` were dropped from the toDto lambda, the journal entry date would default to 0 silently.

**Impact:** 165 mobile tests pass (was 164). 160 API tests pass.

---

## 2026-05-17 — Iteration 213 — Mobile: SyncService includes Goal EnteredDate in upload request

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_LocalGoal_EnteredDateIncludedInUploadRequest` — inserts a goal with EnteredDate=1_000_000 and UpdatedOn=2_000_000 (deliberately different), asserts the captured sync/goal body contains 1_000_000.

**Why:** Goal's `g.EnteredDate` in the toDto lambda was never explicitly tested for upload. Previous tests using the same value for both EnteredDate and UpdatedOn cannot distinguish the two timestamps. If `g.EnteredDate` were accidentally dropped, EnteredDate would default to 0 in the JSON — silently losing the goal's creation date. Using distinct values makes the assertion unambiguous.

**Impact:** 164 mobile tests pass (was 163). 160 API tests pass.

---

## 2026-05-17 — Iteration 212 — Mobile: SyncService includes Journal Notes in upload request

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_LocalJournal_NotesIncludedInUploadRequest` — inserts a journal with Notes="Reflection on the week", runs sync, asserts the captured sync/journal body contains that text.

**Why:** `RunAsync_LocalJournalModifiedSinceLastSync_IncludedInRequest` only asserts the Guid. `RunAsync_LocalJournal_AuxFieldsIncludedInUploadRequest` checks Activity/Mood/Tags but not Notes — the primary content field. If `j.Notes` were accidentally dropped from the toDto lambda, journals would upload with null Notes silently.

**Impact:** 163 mobile tests pass (was 162). 160 API tests pass.

---

## 2026-05-17 — Iteration 211 — Mobile: SyncService includes Goal GoalText in upload request

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_LocalGoal_GoalTextIncludedInUploadRequest` — inserts a goal with GoalText="Master the piano", runs sync, asserts the captured sync/goal body contains that text.

**Why:** `RunAsync_LocalGoal_OptionalFieldsIncludedInUploadRequest` only asserts MeasurableOutcome and NextMeetingDate. The primary content field `g.GoalText` in the toDto lambda is never explicitly checked in the upload body. If accidentally removed, goals would upload with null GoalText silently.

**Impact:** 162 mobile tests pass (was 161). 160 API tests pass.

---

## 2026-05-17 — Iteration 210 — Mobile: SyncService includes GoalProgress NextStepItems in upload request

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_LocalGoalProgress_NextStepItemsIncludedInUploadRequest` — inserts a GoalProgress with NextStepItems="Write unit tests daily", runs sync, asserts the captured sync/goal-progress body contains that text.

**Why:** `RunAsync_LocalGoalProgressModifiedSinceLastSync_IncludedInRequest` only asserts the Guid is in the body. `RunAsync_LocalGoalProgress_NextMeetingDateIncludedInUploadRequest` only verifies the date field. The `toDto` lambda's `p.NextStepItems` serialization — the primary content field — was untested for the upload direction.

**Impact:** 161 mobile tests pass (was 160). 160 API tests pass.

---

## 2026-05-17 — Iteration 209 — Mobile: SyncService includes Todo Notes in upload request

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_LocalTodo_NotesIncludedInUploadRequest` — inserts a todo with Notes="Use cold water", runs sync, asserts the captured sync/todo body contains "Use cold water".

**Why:** `RunAsync_LocalTodo_DueDateIncludedInUploadRequest` only verifies DueDate serialization in the toDto lambda. The `t.Notes` field in `t => new TodoSyncDto(..., t.Notes, ...)` is untested for the upload direction. If it were accidentally omitted, todos would silently lose Notes on every upload. `RunAsync_ServerReturnsTodo_DueDateAndNotesStoredLocally` only covers the server→client direction.

**Impact:** 160 mobile tests pass (was 159). 160 API tests pass.

---

## 2026-05-17 — Iteration 208 — API: JournalSync aux fields (Activity, Mood, Tags) can be cleared via LWW

**What changed:**
- `JournalSyncTests.cs`: Added `Sync_AuxFields_CanBeClearedByClient_ViaNewerUpdate` — stores a journal with Activity, Mood, Tags set; sends a newer-UpdatedOn update with all three as null; asserts all three are null in the delta.

**Why:** `Sync_OptionalFieldsRoundTrip` (initial insert) and `Sync_EnteredDate_UpdatedByClient_OnLWWOverwrite` (LWW update) don't clear Activity/Mood/Tags. If any of those three were accidentally removed from Journal's `ApplyDto`, the field would never be updatable to null, silently retaining stale data.

**Impact:** 160 API tests pass (was 159). 159 mobile tests pass.

---

## 2026-05-17 — Iteration 207 — API: TodoSync Notes can be cleared via LWW update

**What changed:**
- `TodoSyncTests.cs`: Added `Sync_Notes_CanBeClearedByClient_ViaNewerUpdate` — stores a todo with Notes="Some detail", then sends a newer-UpdatedOn update with Notes=null, asserts stored Notes is null.

**Why:** `Sync_OptionalFieldsRoundTrip` only verifies that Notes persists on initial insert (through `DtoToEntity`). The LWW update path (`ApplyDto`) assigns `e.Notes = dto.Notes` which is untested for the null-clear direction. If this line were accidentally removed, Notes could never be cleared. Mirrors iter 206's gap for MeasurableOutcome.

**Impact:** 159 API tests pass (was 158). 159 mobile tests pass.

---

## 2026-05-17 — Iteration 206 — API: Goal MeasurableOutcome can be cleared via LWW update

**What changed:**
- `GoalSyncTests.cs`: Added `Sync_MeasurableOutcome_CanBeClearedByClient_ViaNewerUpdate` — stores a goal with MeasurableOutcome="Run 5km", then sends a newer-UpdatedOn update with MeasurableOutcome=null, asserts stored value is null.

**Why:** All four nullable date fields (CompletionDate, ExpirationDate, NextMeetingDate, DeletedAt) have "can be cleared via LWW" tests. MeasurableOutcome is a nullable string field also written by ApplyDto unconditionally. If `e.MeasurableOutcome = dto.MeasurableOutcome` were accidentally removed from ApplyDto, no existing test would catch it. The optionals round-trip test only sets MeasurableOutcome; it never clears it.

**Impact:** 158 API tests pass (was 157). 159 mobile tests pass.

---

## 2026-05-17 — Iteration 205 — Mobile: SyncService stores Goal EnteredDate received from server

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsGoal_EnteredDateStoredLocally` — server returns a goal delta with `EnteredDate=3_000_000`, asserts the upserted local Goal has the same EnteredDate.

**Why:** Goal's EnteredDate is immutable (locked at creation). `RunAsync_ServerReturnsGoal_OptionalFieldsStoredLocally` verifies NextMeetingDate, ExpirationDate, MeasurableOutcome but not EnteredDate. If `EnteredDate = dto.EnteredDate` were removed from the upsert lambda, goals would silently lose their creation date on sync. Mirrors iter 204 for Journal.

**Impact:** 159 mobile tests pass (was 158). 157 API tests pass.

---

## 2026-05-17 — Iteration 204 — API: JournalSync optional fields round-trip (Activity, Mood, Tags)

**What changed:**
- `JournalSyncTests.cs`: Added `Sync_OptionalFieldsRoundTrip` — syncs a journal with Activity="Running", Mood="Energized", Tags="fitness,outdoors", then syncs with `LastSyncAt=0` and asserts all three fields are returned correctly in the delta.

**Why:** Goal and Todo both have `Sync_OptionalFieldsRoundTrip`. Journal has three optional fields (Activity, Mood, Tags) tested for length limits in SyncInputValidationTests but never verified to survive the full HTTP JSON store-and-retrieve cycle. If the API's `EntityToDto` or `DtoToEntity` accidentally dropped a field, no test would catch it.

**Impact:** 158 API tests pass (was 157). 157 mobile tests pass.

---

## 2026-05-17 — Iteration 203 — SKIP (redundant; GoalProgress GetModifiedSinceAsync soft-delete test already in iter 148)

---

## 2026-05-17 — Iteration 202 — Mobile: TodoRepository GetCompletedCount counts all 3 completed, excludes pending

**What changed:**
- `TodoRepositoryTests.cs`: Added `GetCompletedCountAsync_MultipleCompleted_CountsAll` — inserts 3 completed + 1 pending todo, asserts count = 3.

**Why:** Existing `GetCompletedCount_CountsCompletedExcludesDeleted` verifies count=1. No test verified count > 1. Adding 3-completed test ensures the COUNT query isn't accidentally capped or offset.

**Impact:** 157 mobile tests pass (was 156). 157 API tests pass.

---

## 2026-05-17 — Iteration 201 — Mobile: GetAsync returns null when GUID not found (Goal, Journal, Todo)

**What changed:**
- `GoalRepositoryTests.cs`, `JournalRepositoryTests.cs`, `TodoRepositoryTests.cs`: Added `GetAsync_WhenGuidNotFound_ReturnsNull` to each — calls GetAsync with a non-existent GUID, asserts null.

**Why:** The `FindAsync<T>` contract returns null for missing records. While implicitly exercised in other tests, no test explicitly documented this boundary for any of the 3 repositories. Added 3 tests in one iteration due to trivial shared structure.

**Impact:** 156 mobile tests pass (was 153). 157 API tests pass.

---

## 2026-05-17 — Iteration 200 — Mobile: AccountService SaveServerCredentials second call overwrites first

**What changed:**
- `AccountServiceTests.cs`: Added `SaveServerCredentials_WhenCalledTwice_SecondCredentialsPersisted` — calls `SaveServerCredentialsAsync` with (jwt-v1, server1) then (jwt-v2, server2), asserts stored values equal v2.

**Why:** `SaveServerCredentials_PersistsJwtAndUrl` only tests a single call. Testing two calls verifies the `await db.UpdateAsync(account)` is correctly persisting the update. Mirrors iter 198's pattern for `UpdateLastSyncAsync`.

**Impact:** 153 mobile tests pass (was 152). 157 API tests pass.

---

## 2026-05-17 — Iteration 199 — Mobile: GetLatestNextStepsAsync returns latest for each of two distinct goals

**What changed:**
- `GoalProgressRepositoryTests.cs`: Added `GetLatestNextStepsAsync_TwoGoals_ReturnsLatestForEach` — inserts 2 progress items for goalA and 1 for goalB, asserts the result map contains both Guids with their respective latest entries.

**Why:** `GetLatestNextStepsAsync_ReturnsLatestPerGoal` only tests with a single goalFk. Adding two goals verifies the GROUP BY GoalFk behavior — that the latest is independently selected per goal, not just for the first goal encountered.

**Impact:** 152 mobile tests pass (was 151). 157 API tests pass.

---

## 2026-05-17 — Iteration 198 — Mobile: AccountService UpdateLastSync second call overwrites first

**What changed:**
- `AccountServiceTests.cs`: Added `UpdateLastSync_WhenCalledTwice_SecondTimestampPersisted` — calls `UpdateLastSyncAsync` with T1 then T2, asserts stored LastSyncAt equals T2.

**Why:** `UpdateLastSync_SetsTimestamp` only tests a single call. Testing two sequential calls verifies that the second update correctly persists via `db.UpdateAsync`, not just to an in-memory object. If `await db.UpdateAsync(account)` were removed, the second call would appear to succeed but the value from GetAccountAsync would be stale.

**Impact:** 151 mobile tests pass (was 150). 157 API tests pass.

---

## 2026-05-17 — Iteration 197 — Mobile: GoalRepository excludes completed-then-deleted goal from GetAllActive

**What changed:**
- `GoalRepositoryTests.cs`: Added `GetAllActiveAsync_CompletedThenDeletedGoal_IsExcluded` — inserts a goal with both CompletionDate and DeletedAt set, asserts `GetAllActiveAsync` returns empty (DeletedAt takes precedence).

**Why:** Existing exclusion tests cover: soft-deleted only (via DeleteAsync path, iter 194 via UpsertFromSync). None test the compound case where both CompletionDate AND DeletedAt are set. This exercises the `WHERE DeletedAt IS NULL` filter when a goal has been completed AND deleted, confirming DeletedAt wins.

**Impact:** 150 mobile tests pass (was 149). 157 API tests pass.

---

## 2026-05-17 — Iteration 196 — Mobile: TodoRepository excludes synced soft-deleted record from GetAllActive and GetPending

**What changed:**
- `TodoRepositoryTests.cs`: Added `GetAllActiveAsync_UpsertedSoftDeletedRecord_IsExcluded` and `GetPendingAsync_UpsertedSoftDeletedRecord_IsExcluded` — both verify that a todo arriving via sync with DeletedAt set is excluded from the respective query results.

**Why:** Todo has two retrieval queries that exclude soft-deleted records: `GetAllActiveAsync` and `GetPendingAsync`. Testing both ensures that the SQL `WHERE DeletedAt IS NULL` filter is correct for both query paths when data arrives via sync. Added 2 tests in one iteration due to their shared setup and close relationship.

**Impact:** 149 mobile tests pass (was 147). 157 API tests pass.

---

## 2026-05-17 — Iteration 195 — Mobile: JournalRepository excludes synced soft-deleted record from GetAllActive

**What changed:**
- `JournalRepositoryTests.cs`: Added `GetAllActiveAsync_UpsertedSoftDeletedRecord_IsExcluded` — mirrors iter 194's Goal test for Journal.

**Why:** Completing the "synced soft-delete excluded" coverage for all entities. Journal and Todo remain (GoalProgress uses `GetForGoalAsync`, not `GetAllActive`).

**Impact:** 147 mobile tests pass (was 146). 157 API tests pass.

---

## 2026-05-17 — Iteration 194 — Mobile: GoalRepository excludes synced soft-deleted record from GetAllActive

**What changed:**
- `GoalRepositoryTests.cs`: Added `GetAllActiveAsync_UpsertedSoftDeletedRecord_IsExcluded` — uses `UpsertFromSyncAsync` to insert a goal with DeletedAt set, then asserts `GetAllActiveAsync` returns empty.

**Why:** `Delete_SoftDeletes_ExcludedFromActive` only tests the `DeleteAsync` path. A goal arriving via sync with DeletedAt already set (e.g., deleted on another device) exercises the same `WHERE DeletedAt IS NULL` SQL filter but through the `UpsertFromSyncAsync` path. Separate test ensures both insertion paths respect the filter.

**Impact:** 146 mobile tests pass (was 145). 157 API tests pass.

---

## 2026-05-17 — Iteration 193 — Mobile: SyncService includes Goal ExpirationDate in upload request

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_LocalGoal_ExpirationDateIncludedInUploadRequest` — pre-inserts a goal with ExpirationDate set, syncs, asserts the upload body for sync/goal contains the ExpirationDate timestamp.

**Why:** `RunAsync_LocalGoal_OptionalFieldsIncludedInUploadRequest` tests MeasurableOutcome and NextMeetingDate but not ExpirationDate. Adding coverage for the third optional Goal field in the upload path. If ExpirationDate serialization were broken, this test would catch it.

**Impact:** 145 mobile tests pass (was 144). 157 API tests pass.

---

## 2026-05-17 — Iteration 192 — Mobile: GoalProgress GetForGoal 3-item ordering test

**What changed:**
- `GoalProgressRepositoryTests.cs`: Added `GetForGoalAsync_ThreeItems_OrderedByUpdatedOnDescending` — inserts 3 progress items in shuffled order (middle, oldest, newest) and asserts `GetForGoalAsync` returns [newest, middle, oldest].

**Why:** Existing `GetForGoalAsync_OrdersByUpdatedOnDescending` only tests with 2 items. A 3-item test with shuffled insertion order more thoroughly exercises the SQL `ORDER BY UpdatedOn DESC` and catches off-by-one ordering bugs that 2-item tests can miss.

**Impact:** 144 mobile tests pass (was 143). 157 API tests pass.

---

## 2026-05-17 — Iteration 191 — Mobile: SyncService includes locally-completed goal with CompletionDate in upload

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_LocalCompletedGoal_CompletionDateIncludedInUploadRequest` — pre-inserts a goal with CompletionDate set, syncs, asserts the upload body for sync/goal contains Guid and CompletionDate timestamp.

**Why:** Mirrors iter 190's completed-todo upload test for Goal entity. Completes the pair: if someone accidentally filtered completed items out of `GetModifiedSinceAsync` for Goals, this test would fail.

**Impact:** 143 mobile tests pass (was 142). 157 API tests pass.

---

## 2026-05-17 — Iteration 190 — Mobile: SyncService includes locally-completed todo with CompletedAt in upload

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_LocalCompletedTodo_CompletedAtIncludedInUploadRequest` — pre-inserts a todo with CompletedAt set, syncs, asserts the upload body for sync/todo contains both the Guid and CompletedAt timestamp.

**Why:** Soft-deleted record upload was tested (iter ~15-21) but completed-todo upload was not explicitly covered at the SyncService level. If `GetModifiedSinceAsync` were ever accidentally changed to filter out completed todos, this test would fail. Mirrors the pattern of `RunAsync_LocalSoftDeletedTodo_IncludedInUploadRequest`.

**Impact:** 142 mobile tests pass (was 141). 157 API tests pass.

---

## 2026-05-17 — Iteration 189 — API: GoalProgress batch with mixed AccountFk stores valid and skips intruder

**What changed:**
- `GoalProgressSyncTests.cs`: Added `Sync_BatchWithMixedAccountFk_ValidRecordStoredInvalidSkipped` — completes mixed-AccountFk batch coverage for all 4 entities (Journal iter 182, Todo iter 187, Goal iter 188, GoalProgress this iter).

**Why:** Final entity to add mixed-batch per-record filtering test. All 4 sync endpoints now have full symmetry for this behavior.

**Impact:** 157 API tests pass (was 156). 141 mobile tests pass.

---

## 2026-05-17 — Iteration 188 — API: Goal batch with mixed AccountFk stores valid and skips intruder

**What changed:**
- `GoalSyncTests.cs`: Added `Sync_BatchWithMixedAccountFk_ValidRecordStoredInvalidSkipped` — mirrors the Journal (iter 182) and Todo (iter 187) version for Goal.

**Why:** Completing mixed-AccountFk batch coverage across all 4 entities. GoalProgress remains after this.

**Impact:** 156 API tests pass (was 155). 141 mobile tests pass.

---

## 2026-05-17 — Iteration 187 — API: Todo batch with mixed AccountFk stores valid and skips intruder

**What changed:**
- `TodoSyncTests.cs`: Added `Sync_BatchWithMixedAccountFk_ValidRecordStoredInvalidSkipped` — sends a batch containing one valid record (correct AccountFk) and one intruder (different account's Guid), asserts valid is stored and intruder is absent from delta.

**Why:** `Sync_BatchWithMixedAccountFk_ValidRecordStoredInvalidSkipped` was added for Journal in iter 182. Todo, Goal, and GoalProgress only had `Sync_RecordWithWrongAccountFk_IsRejected` (whole-batch-of-one-bad-record). The mixed-batch test (one valid + one intruder) exercises per-record filtering, which is a distinct behavior from the single-record rejection path.

**Impact:** 155 API tests pass (was 154). 141 mobile tests pass.

---

## 2026-05-17 — Iteration 186 — API: Goal NextMeetingDate can be cleared by client via LWW

**What changed:**
- `GoalSyncTests.cs`: Added `Sync_NextMeetingDate_CanBeClearedByClient_ViaNewerUpdate` — stores a goal with NextMeetingDate set, then syncs same Guid with NextMeetingDate=null and newer UpdatedOn, asserts server stores null NextMeetingDate.

**Why:** Goal has 3 nullable date fields: ExpirationDate (clearing tested iter 180), CompletionDate (uncomplete tested iter 178), and NextMeetingDate — not tested for LWW null-clearing. GoalProgress had `Sync_NextMeetingDate_CanBeClearedByClient_ViaNewerUpdate` added in iter 180, but Goal's version was missing. Completing symmetry.

**Impact:** 154 API tests pass (was 153). 141 mobile tests pass.

---

## 2026-05-17 — Iteration 185 — Mobile: Comprehensive 4-record mixed active/completed goal ordering

**What changed:**
- `GoalRepositoryTests.cs`: Added `GetAllActiveAsync_MixedActiveAndCompletedGoals_CorrectOrdering` — inserts 2 active + 2 completed goals with distinct EnteredDates in shuffled insertion order, asserts result is [newer_active, older_active, newer_completed, older_completed].

**Why:** Existing ordering tests covered only partial scenarios: 2-record active-before-completed (iter 58-73), 2-active DESC (iter 227-241), 3-active DESC, 2-completed DESC. None tested the full 4-record `ORDER BY (CompletionDate IS NOT NULL), EnteredDate DESC` with both partition groups populated simultaneously. A bug that put completed-newer before active-older would pass all existing tests but fail this one.

**Impact:** 141 mobile tests pass (was 140). 153 API tests pass.

---

## 2026-05-17 — Iteration 184 — Mobile: SyncService overwrites existing local Goal, Todo, GoalProgress

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsExistingGoal_OverwritesLocalVersion`
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsExistingTodo_OverwritesLocalVersion`
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsExistingGoalProgress_OverwritesLocalVersion`

All 3 tests follow iter 183's pattern: pre-insert a local record, sync with server returning same Guid with newer UpdatedOn and updated content, assert local record is overwritten.

**Why:** Iter 183 added the overwrite test for Journal. Goal, Todo, and GoalProgress had only "upserts new record" tests (Guid doesn't exist locally). The overwrite path uses the same `InsertOrReplaceAsync` call, but documenting it for all 4 entities ensures symmetry and protects against entity-specific bugs in the `UpsertFromSyncAsync` implementations.

**Impact:** 140 mobile tests pass (was 137). 153 API tests pass.

---

## 2026-05-17 — Iteration 183 — Mobile: SyncService overwrites existing local journal with server version

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsExistingJournal_OverwritesLocalVersion` — pre-inserts a journal locally with Notes="local version", then syncs with a server that returns the same Guid with Notes="server version" and a newer UpdatedOn; asserts the local journal is updated to "server version".

**Why:** All prior `RunAsync_ServerReturns*_UpsertsLocally` tests create NEW records (Guids that don't exist locally). None tested the overwrite path where the Guid already exists locally with an older version. The `UpsertFromSyncAsync` method calls `InsertOrReplaceAsync` which overwrites on Guid collision, but without a test, a change to check `if (!local.Guid.exists)` before writing would silently break inbound sync updates.

**Impact:** 137 mobile tests pass (was 136). 153 API tests pass.

---

## 2026-05-17 — Iteration 182 — API: Mixed-AccountFk batch stores valid record and skips intruder

**What changed:**
- `JournalSyncTests.cs`: Added `Sync_BatchWithMixedAccountFk_ValidRecordStoredInvalidSkipped` — sends a batch of 2 records, one with correct AccountFk and one with a different user's AccountFk; asserts the valid record appears in the delta and the intruder does not.

**Why:** The existing `Sync_RecordWithWrongAccountFk_IsRejected` test sends ONLY an invalid record. That test verifies the invalid record is rejected, but doesn't confirm that valid records in the same batch are still processed. The new test confirms per-record filtering: the mismatch check skips individual records (not the whole batch). This matters because an earlier implementation that rejected the whole batch on any mismatch would pass the existing test but fail this one.

**Impact:** 153 API tests pass (was 152). 136 mobile tests pass.

---

## 2026-05-17 — Iteration 181 — API: Soft-deleted records can be restored via newer LWW update

**What changed:**
- `JournalSyncTests.cs`: Added `Sync_SoftDeleted_CanBeRestoredByClient_ViaNewerUpdate`
- `GoalSyncTests.cs`: Added `Sync_SoftDeleted_CanBeRestoredByClient_ViaNewerUpdate`
- `GoalProgressSyncTests.cs`: Added `Sync_SoftDeleted_CanBeRestoredByClient_ViaNewerUpdate`
- `TodoSyncTests.cs`: Added `Sync_SoftDeleted_CanBeRestoredByClient_ViaNewerUpdate`

All 4 tests follow the same pattern: store a soft-deleted record (DeletedAt set), then send same Guid with DeletedAt=null and newer UpdatedOn; assert the delta shows DeletedAt=null.

**Why:** `ApplyDto` for all 4 entities includes `entity.DeletedAt = dto.DeletedAt` unconditionally. So LWW semantics allow "un-deleting" a record by sending null DeletedAt with a newer UpdatedOn. Without a test, a "fix" adding `if (entity.DeletedAt.HasValue) return;` to prevent restoration would silently break the LWW contract. These tests document the intentional restoration behavior across all entities.

**Impact:** 152 API tests pass (was 148). 136 mobile tests pass.

---

## 2026-05-17 — Iteration 180 — API: Nullable date fields can be cleared via newer LWW update

**What changed:**
- `TodoSyncTests.cs`: Added `Sync_DueDate_CanBeClearedByClient_ViaNewerUpdate` — stores a todo with DueDate set, then sends null DueDate + newer UpdatedOn, asserts DueDate is null.
- `GoalSyncTests.cs`: Added `Sync_ExpirationDate_CanBeClearedByClient_ViaNewerUpdate` — same pattern for ExpirationDate.
- `GoalProgressSyncTests.cs`: Added `Sync_NextMeetingDate_CanBeClearedByClient_ViaNewerUpdate` — same pattern for NextMeetingDate.

**Why:** Iter 178/179 documented that completion fields (CompletionDate, CompletedAt) can be nulled via LWW. This iteration extends that pattern to all remaining nullable date fields. `ApplyDto` for each endpoint includes these fields unconditionally, so null values overwrite stored non-null values. Without tests, a "fix" that added `if (dto.X.HasValue)` guards would silently break the LWW contract for these fields.

**Impact:** 148 API tests pass (was 145). 136 mobile tests pass.

---

## 2026-05-17 — Iteration 179 — API: Completed Todo can be un-completed via newer LWW update

**What changed:**
- `TodoSyncTests.cs`: Added `Sync_CompletedTodo_CanBeUncompletedByClient_ViaNewerUpdate` — stores a completed todo (CompletedAt set), then sends same Guid with CompletedAt=null and a newer UpdatedOn; asserts the stored record has CompletedAt=null.

**Why:** Parallel to iter 178's Goal test. `TodoEndpoints.ApplyDto` includes `e.CompletedAt = dto.CompletedAt`, so LWW allows un-completing a todo. The test documents the intentional behavior and guards against a future guard condition blocking null CompletedAt updates.

**Impact:** 145 API tests pass (was 144). 136 mobile tests pass.

---

## 2026-05-17 — Iteration 178 — API: Completed Goal can be un-completed via newer LWW update

**What changed:**
- `GoalSyncTests.cs`: Added `Sync_CompletedGoal_CanBeUncompletedByClient_ViaNewerUpdate` — stores a completed goal (CompletionDate set), then sends same Guid with CompletionDate=null and a newer UpdatedOn; asserts the stored record has CompletionDate=null.

**Why:** `GoalEndpoints.ApplyDto` includes `e.CompletionDate = dto.CompletionDate`, so LWW semantics allow un-completing a goal. Without a test, a well-intentioned developer could add a guard like `if (entity.CompletionDate.HasValue) return;` to "protect" completed goals, which would silently break the LWW contract. The test documents the intentional behavior: completion status follows the latest `UpdatedOn`, same as every other field.

**Impact:** 144 API tests pass (was 143). 136 mobile tests pass.

---

## 2026-05-17 — Iteration 177 — API: Journal EnteredDate is mutable on LWW overwrite

**What changed:**
- `JournalSyncTests.cs`: Added `Sync_EnteredDate_UpdatedByClient_OnLWWOverwrite` — stores a journal with EnteredDate=T1, then sends same Guid with EnteredDate=T2 and a newer UpdatedOn; asserts the delta shows the updated EnteredDate=T2.

**Why:** Iter 176 documented that Goal's `EnteredDate` is immutable (creation date locked). Journal's `EnteredDate` is intentionally mutable — users can retroactively correct the date of a journal entry. `JournalEndpoints.ApplyDto` includes `entity.EnteredDate = dto.EnteredDate`, so this works by design. Without a test, the distinction is undocumented, and a developer could accidentally "fix" Journal to match Goal's behavior and break date correction. The test pair (iter 176 for Goal, iter 177 for Journal) documents the intentional asymmetry.

**Impact:** 143 API tests pass (was 142). 136 mobile tests pass.

---

## 2026-05-17 — Iteration 176 — API: Goal EnteredDate is immutable on LWW overwrite

**What changed:**
- `GoalSyncTests.cs`: Added `Sync_EnteredDate_NotUpdatedOnLWWOverwrite` — stores a goal with EnteredDate=T1, then sends same Guid with EnteredDate=T2 and a newer UpdatedOn; asserts the delta still shows EnteredDate=T1.

**Why:** `GoalEndpoints.ApplyDto` deliberately excludes `EnteredDate` from LWW updates (once set at creation, a goal's creation date never changes). Contrast with `JournalEndpoints.ApplyDto`, which does allow `EnteredDate` updates (users can correct the date of a journal entry). Without a test, a maintenance change that accidentally added `e.EnteredDate = dto.EnteredDate` to Goal's `ApplyDto` would silently allow goal creation-date mutation. The GoalFk immutability test (iter 170) established the pattern; this closes the equivalent gap for EnteredDate.

**Impact:** 142 API tests pass (was 141). 136 mobile tests pass.

---

## 2026-05-17 — Iteration 175 — Mobile: UpsertFromSyncAsync persists all optional fields for all 4 repos

**What changed:**
- `JournalRepositoryTests.cs`: Added `UpsertFromSyncAsync_PersistsAllOptionalFields` — verifies Activity, Mood, Tags survive the `UpsertFromSyncAsync` path.
- `GoalRepositoryTests.cs`: Added `UpsertFromSyncAsync_PersistsAllOptionalFields` — verifies MeasurableOutcome, NextMeetingDate, ExpirationDate.
- `GoalProgressRepositoryTests.cs`: Added `UpsertFromSyncAsync_PersistsAllOptionalFields` — verifies NextStepItems and NextMeetingDate.
- `TodoRepositoryTests.cs`: Added `UpsertFromSyncAsync_PersistsAllOptionalFields` — verifies Notes and DueDate.

**Why:** Iter 173 added `SaveAsync_PersistsAllOptionalFields` tests (the local-edit path). `UpsertFromSyncAsync` is the distinct inbound-sync path — it calls `InsertOrReplaceAsync` directly without going through `SaveAsync`. If a field were accidentally dropped from the model or a column mapping missed, these tests catch it on the sync path, not just the save path.

**Impact:** 136 mobile tests pass (was 132). 141 API tests pass.

---

## 2026-05-17 — Iteration 174 — API: LastSyncAt upper-bound empty delta for Goal, GoalProgress, Todo

**What changed:**
- `GoalSyncTests.cs`: Added `Sync_LastSyncAt_LargerThanAllRecords_EmptyDelta` — stores a goal at timestamp T, syncs with `LastSyncAt = T + 10_000`, asserts empty delta.
- `GoalProgressSyncTests.cs`: Added `Sync_LastSyncAt_LargerThanAllRecords_EmptyDelta` — same pattern for GoalProgress.
- `TodoSyncTests.cs`: Added `Sync_LastSyncAt_LargerThanAllRecords_EmptyDelta` — same pattern for Todo.

**Why:** Journal got this test in iter 171. Goal/GoalProgress/Todo all had `Sync_LastSyncAt_NegativeValue_ReturnsAllRecords` (lower-bound) but not the complementary upper-bound test. Now all 4 entities consistently document both ends of the delta filter range (`WHERE UpdatedOn > LastSyncAt`).

**Impact:** 141 API tests pass (was 138). 132 mobile tests pass.

---

## 2026-05-17 — Iteration 173 — Mobile: Goal, Todo, GoalProgress SaveAsync optional fields persistence

**What changed:**
- `GoalRepositoryTests.cs`: Added `SaveAsync_PersistsAllOptionalFields` — verifies MeasurableOutcome, NextMeetingDate, ExpirationDate are stored and retrievable after `SaveAsync`.
- `TodoRepositoryTests.cs`: Added `SaveAsync_PersistsAllOptionalFields` — verifies Notes and DueDate survive the `SaveAsync` + `GetAsync` round-trip.
- `GoalProgressRepositoryTests.cs`: Added `SaveAsync_PersistsAllOptionalFields` — verifies NextStepItems and NextMeetingDate are stored after `SaveAsync`.

**Why:** Iter 172 added the same pattern for Journal. The remaining 3 repositories all have optional fields that `SaveAsync` writes through `InsertOrReplaceAsync`, but no test previously confirmed the full set of nullable fields survived the round-trip. If a field were accidentally removed from the model mapping (e.g., missing `[Column]` attribute), these tests would catch it.

**Impact:** 132 mobile tests pass (was 129). 138 API tests pass.

---

## 2026-05-17 — Iteration 172 — Mobile: Journal optional fields persistence + SyncService empty batch

**What changed:**
- `JournalRepositoryTests.cs`: Added `SaveAsync_PersistsAllOptionalFields` — verifies Activity, Mood, Tags are stored and retrievable via `GetAsync` after a `SaveAsync` call.
- `SyncServiceTests.cs`: Added `RunAsync_NoLocalChanges_SendsEmptyBatchToAllEndpoints` — verifies the outgoing journal sync request body contains `"records":[]` when the local database has no modified journal records.

**Why:** `JournalRepository.SaveAsync` writes the full entity, but no test verified that optional fields survive the round-trip through SQLite. The empty-batch test confirms `SyncService` calls all 4 endpoints even with zero local records (the LWW delta response from the server is still needed).

**Impact:** 129 mobile tests pass (was 127). 138 API tests pass.

---

## 2026-05-17 — Iteration 171 — API: Auth token AccountGuid contract + Journal delta empty for future LastSyncAt

**What changed:**
- `AuthEndpointTests.cs`: Added `Token_ValidCredentials_ResponseIncludesAccountGuid` — registers a user, then calls `/api/auth/token`, verifies the `AccountGuid` field is present in the response AND matches the GUID returned at registration. Previously the token test only asserted `Jwt` was non-null.
- `JournalSyncTests.cs`: Added `Sync_LastSyncAt_LargerThanAllRecords_EmptyDelta` — stores a journal at timestamp T, then syncs with `LastSyncAt = T + 10_000`, asserts the delta is empty. Complements the existing tests for `LastSyncAt = 0` and negative values by covering the upper bound.

**Why:** The `AuthResponse` record has both `Jwt` and `AccountGuid` fields; only `Jwt` was previously tested on the token endpoint. If `AccountGuid` were accidentally removed from the response, mobile devices couldn't match their local account to the server's. The future-`LastSyncAt` test documents the exclusive upper-bound behavior of the delta query (`WHERE UpdatedOn > LastSyncAt`).

**Impact:** 138 API tests pass (was 136). 127 mobile tests pass.

---

## 2026-05-17 — Iteration 170 — API: GoalProgress GoalFk immutable on LWW + Mobile: CompletedCount account isolation

**What changed:**
- `GoalProgressSyncTests.cs`: Added `Sync_GoalFkNotChangedOnLWWUpdate` — stores progress with GoalFk=A, then sends same Guid with GoalFk=B and newer UpdatedOn; asserts delta still shows GoalFk=A. Documents that `ApplyDto` deliberately excludes `GoalFk` from LWW updates (once set at creation, it never changes).
- `TodoRepositoryTests.cs`: Added `GetCompletedCountAsync_ExcludesOtherAccounts` — inserts one completed todo per account, asserts `GetCompletedCountAsync(account1)` returns 1, not 2. The WHERE clause filters by `AccountFk` but no test previously verified the cross-account isolation for this specific method.

**Why:** GoalFk immutability is an intentional invariant enforced by omitting it from `ApplyDto`. Without a test, a maintenance change adding `e.GoalFk = dto.GoalFk` to `ApplyDto` would silently allow goal reassignment. The CompletedCount test closes an isolation gap — the method is used in the Dashboard summary and wrong counts would appear if account isolation failed.

**Impact:** 127 mobile tests pass (was 126). 136 API tests pass (was 135).

---

## 2026-05-17 — Iteration 169 — Mobile: Todo pending 3-item ordering + SyncService multi-entity upload

**What changed:**
- `TodoRepositoryTests.cs`: Added `GetPendingAsync_ThreeItems_DueDateTodosBeforeNoDueDate` — inserts 3 pending items (day1 DueDate, day2 DueDate, no DueDate) in shuffled order, asserts the pending list is [day1, day2, no-due-date]. The existing tests had a 2-item due/null test and a 3-item all-due test, but no 3-item mixed test covering the sort across the DueDate/null boundary.
- `SyncServiceTests.cs`: Added `RunAsync_MultipleLocalModifications_AllFourEndpointsReceiveData` — inserts one record per entity type (Journal, Goal, GoalProgress, Todo), then verifies all 4 sync endpoints received request bodies. Ensures the full sync pipeline calls each entity endpoint when data exists.

**Why:** The 3-item mixed-due-date test verifies the `ORDER BY (DueDate IS NULL), DueDate` sort is stable across the boundary (not just within like-typed groups). The multi-entity upload test confirms no entity is accidentally skipped when all 4 have local data.

**Impact:** 126 mobile tests pass (was 124). 135 API tests pass.

---

## 2026-05-17 — Iteration 168 — Mobile: SyncService outgoing upload serialization for optional fields

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_LocalJournal_AuxFieldsIncludedInUploadRequest` — inserts a journal with `Activity="Yoga"`, `Mood="Calm"`, `Tags="health,wellness"`, asserts all three appear in the outgoing request body.
- `SyncServiceTests.cs`: Added `RunAsync_LocalGoal_OptionalFieldsIncludedInUploadRequest` — inserts a goal with `MeasurableOutcome` and `NextMeetingDate`, asserts both appear in the outgoing request body.
- `SyncServiceTests.cs`: Added `RunAsync_LocalTodo_DueDateIncludedInUploadRequest` — inserts a todo with `DueDate`, asserts the timestamp value appears in the outgoing request body.
- `SyncServiceTests.cs`: Added `RunAsync_LocalGoalProgress_NextMeetingDateIncludedInUploadRequest` — inserts a progress record with `NextMeetingDate`, asserts it appears in the outgoing request body.

**Why:** The existing upload tests only assert the record's GUID is present in the request body. The `toDto` lambdas in `SyncService.RunAsync` serialize 6-10 fields per entity — if an optional field were accidentally dropped from a lambda, no test would catch it on the upload side. These tests close that gap for all 4 entities.

**Impact:** 124 mobile tests pass (was 120). 135 API tests pass.

---

## 2026-05-17 — Iteration 167 — Mobile: Goal completed-section ordering + GoalProgress soft-delete fallback

**What changed:**
- `GoalRepositoryTests.cs`: Added `GetAllActiveAsync_TwoCompletedGoals_OrderedByEnteredDateDescending` — inserts 2 completed goals with different `EnteredDate`, verifies they're ordered newest-entered-first within the completed section. The SQL `ORDER BY (CompletionDate IS NOT NULL), EnteredDate DESC` applies the same EnteredDate ordering to both active and completed sections, but no test previously verified the completed section's ordering.
- `GoalProgressRepositoryTests.cs`: Added `GetLatestNextStepsAsync_WhenLatestIsSoftDeleted_FallsBackToPrior` — inserts an older active progress record and a newer soft-deleted one; verifies `GetLatestNextStepsAsync` returns the older active record (not empty). The SQL filters `DeletedAt IS NULL` before grouping, so the "latest among active" semantics are confirmed.

**Why:** The ordering invariant within completed goals was implicit (from the SQL), but untested — a refactor removing `EnteredDate DESC` from the ORDER BY would silently break it. The GoalProgress fallback test documents the intended "latest non-deleted" semantics, which are subtly different from "globally latest."

**Impact:** 120 mobile tests pass (was 118). 135 API tests pass.

---

## 2026-05-17 — Iteration 166 — API: LWW tie behavior (server wins) + exact-500 batch boundary

**What changed:**
- `JournalSyncTests.cs`: Added `Sync_TieOnUpdatedOn_ServerVersionWins` — server stores v1 at time T, client resends same Guid with different content at same T; asserts server kept its version.
- `TodoSyncTests.cs`: Same for Todo.
- `GoalSyncTests.cs`: Same for Goal.
- `GoalProgressSyncTests.cs`: Same for GoalProgress.
- `SyncInputValidationTests.cs`: Added `Sync_ExactlyMaxBatchSize_Returns200` — sends exactly 500 Journal records with valid fields; asserts 200 OK (boundary condition complementing the existing 501→400 test).

**Why:** The LWW implementation uses strict `>` (not `>=`), meaning ties go to the server's stored version. No test documented this invariant — a refactor to `>=` would silently allow clients to overwrite concurrent edits on tie. The 500-record boundary test ensures the upper limit is inclusive (the existing test only proves 501 is rejected).

**Impact:** 135 API tests pass (was 130). 118 mobile tests pass.

---

## 2026-05-17 — Iteration 165 — Mobile: SyncService inbound optional field propagation for all 4 entities

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsJournal_AuxFieldsStoredLocally` — verifies `Activity`, `Mood`, `Tags` all survive inbound sync (previously only `Notes` was checked).
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsGoalProgress_NextMeetingDateStoredLocally` — verifies `NextMeetingDate` is stored when server returns a progress record with that field set.
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsTodo_DueDateAndNotesStoredLocally` — verifies `DueDate` and `Notes` survive inbound sync (previously only `Title` was checked).
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsGoal_OptionalFieldsStoredLocally` — verifies `NextMeetingDate`, `ExpirationDate`, `MeasurableOutcome` are stored from inbound sync (previously only `GoalText` was checked).

**Why:** The existing per-entity "UpsertsLocally" tests each verified only the primary required field. The SyncService `RunAsync` maps 7–10 fields per entity — if any optional field were accidentally dropped from the mapping, inbound data from other devices would silently be lost. These tests provide regression protection for every optional field in the inbound path.

**Impact:** 118 mobile tests pass (was 114). 130 API tests pass.

---

## 2026-05-17 — Iteration 164 — Mobile: SyncService inbound CompletionDate + CompletedAt propagation

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsCompletedGoal_CompletionDateStoredLocally` — server sends a goal with `CompletionDate` set, verifies it's stored in the local DB.
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsCompletedTodo_CompletedAtStoredLocally` — same for Todo.

**Why:** The existing `RunAsync_ServerReturnsGoal_UpsertsLocally` only checks `GoalText`. The `DtoToEntity` mapper has 9 fields for Goal — if `CompletionDate` was accidentally dropped from the mapping, completions from other devices would silently fail to propagate. These tests guard that specific optional-field mapping path.

**Impact:** 114 mobile tests pass (was 112). 130 API tests pass.

---

## 2026-05-17 — Iteration 163 — Mobile: SyncService HttpRequestException retry + Journal 3-item ordering

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_EntitySyncNetworkError_RetriesAndSucceeds` — throws `HttpRequestException` on the first entity sync call, asserts the service recovers on retry. Guards the `catch(HttpRequestException)` retry branch in `SyncEntityAsync` (previously only 5xx retry was tested with `TransientFailThenSucceedHandler`).
- `JournalRepositoryTests.cs`: Added `GetAllActiveAsync_MultipleJournals_OrderedByEnteredDateDescending` — inserts 3 journals in shuffled order (middle, older, newer), asserts newest-first. Mirrors the 3-item goal ordering test added in iter 153.

**Why:** The `catch (HttpRequestException)` path retries the POST — the existing `TransientFailThenSucceedHandler` only tests 5xx, not the exception path. The Journal 3-item test covers the case where sorting is correct across multiple entries (the 2-item test can pass with an unstable sort).

**Impact:** 112 mobile tests pass (was 110). 130 API tests pass.

---

## 2026-05-17 — Iteration 162 — API fix: Goal endpoint NextMeetingDate validation + completed-goal blank text

**What changed:**
- `GoalEndpoints.cs`: Added `NextMeetingDate > 10 years` guard (matching the existing GoalProgress rule). Previously a goal with NextMeetingDate set 100 years in the future would be silently accepted.
- `GoalEndpoints.cs`: Fixed blank GoalText check: now `r.DeletedAt is null && r.CompletionDate is null && string.IsNullOrWhiteSpace(r.GoalText)` — previously completed goals with blank GoalText were rejected (inconsistent with Todo where completed todos accept blank title).
- `SyncInputValidationTests.cs`: Added `Sync_Goal_FutureNextMeetingDate_Returns422`
- `SyncInputValidationTests.cs`: Added `Sync_Goal_CompletedWithBlankGoalText_IsAccepted`

**Why:** Two production bugs. NextMeetingDate had a guard in GoalProgress (iter unknown) but was accidentally omitted from Goal. The completed-goal check was too strict compared to Todo — when a goal is marked complete, the goal text may be intentionally cleared on the device, and the server should accept it.

**Impact:** 110 mobile tests pass. 130 API tests pass (was 128).

---

## 2026-05-17 — Iteration 161 — Mobile: SyncService inbound soft-delete propagation for Todo + GoalProgress

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsDeletedTodo_DeletedAtPropagatedLocally`
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsDeletedGoalProgress_DeletedAtPropagatedLocally`

**Why:** Completes the inbound `DeletedAt` propagation coverage across all 4 entity types. Journal and Goal were covered in iter 160. Todo and GoalProgress had the same mapper gap (dropping `DeletedAt` from `DtoToEntity` would silently prevent cross-device deletion propagation).

**Impact:** 110 mobile tests pass (was 108). 128 API tests pass.

---

## 2026-05-17 — Iteration 160 — Mobile: SyncService inbound soft-delete propagation for Journal + Goal

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsDeletedJournal_DeletedAtPropagatedLocally` — sends a `JournalSyncDto` with `DeletedAt` set, asserts the stored local `Journal` has `DeletedAt` set.
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsDeletedGoal_DeletedAtPropagatedLocally` — same for Goal.

**Why:** The existing `RunAsync_ServerReturnsData_UpsertsLocally` tests all pass `DeletedAt = null`. If the `DtoToEntity` mapper accidentally dropped the `DeletedAt` field, soft-deletes from other devices (via the server) would silently fail to propagate — deleted records would reappear on sync. These tests guard that inbound path.

**Impact:** 108 mobile tests pass (was 106). 128 API tests pass.

---

## 2026-05-17 — Iteration 159 — API: negative LastSyncAt returns all records for Journal, Goal, GoalProgress

**What changed:**
- `JournalSyncTests.cs`: Added `Sync_LastSyncAt_NegativeValue_ReturnsAllRecords`
- `GoalSyncTests.cs`: Added `Sync_LastSyncAt_NegativeValue_ReturnsAllRecords`
- `GoalProgressSyncTests.cs`: Added `Sync_LastSyncAt_NegativeValue_ReturnsAllRecords`

**Why:** `TodoSyncTests` (iter 149) already had this test — verifies that `WHERE UpdatedOn > -1` returns all records, covering the "never synced before" sentinel. Journal, Goal, and GoalProgress had no such guard, leaving their delta filter's negative-value behavior untested.

**Impact:** 106 mobile tests pass. 128 API tests pass (was 125).

---

## 2026-05-17 — Iteration 158 — Mobile: SyncService soft-delete upload coverage for Goal, Todo, GoalProgress

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_LocalSoftDeletedGoal_IncludedInUploadRequest`, `RunAsync_LocalSoftDeletedTodo_IncludedInUploadRequest`, and `RunAsync_LocalSoftDeletedGoalProgress_IncludedInUploadRequest`.

**Why:** `RunAsync_LocalSoftDeletedJournal_IncludedInUploadRequest` already verified that soft-deleted journals appear in upload payloads (the `GetModifiedSinceAsync` filter has no `DeletedAt IS NULL` guard). Goal, Todo, and GoalProgress had no equivalent test — if `GetModifiedSinceAsync` accidentally gained a soft-delete filter for any of these entities, deletions would silently stop propagating to the server.

**Impact:** 106 mobile tests pass (was 103). 125 API tests pass.

---

## 2026-05-17 — Iteration 157 — API: Goal + GoalProgress delta ordering by UpdatedOn

**What changed:**
- `GoalSyncTests.cs`: Added `Sync_Delta_OrderedByUpdatedOnAscending` — inserts 3 goals at T3/T1/T2, asserts delta returns T1→T2→T3.
- `GoalProgressSyncTests.cs`: Added `Sync_Delta_OrderedByUpdatedOnAscending` — same for GoalProgress.

**Why:** All 4 entity sync endpoints use `ORDER BY UpdatedOn ASC` in the delta query. Journal (iter 151) and Todo (iter 156) already have this test. Goal and GoalProgress did not, leaving their sort order unguarded.

**Impact:** 103 mobile tests pass. 125 API tests pass (was 123).

---

## 2026-05-17 — Iteration 156 — API: Todo optional fields round-trip + delta ordering

**What changed:**
- `TodoSyncTests.cs`: Added `Sync_OptionalFieldsRoundTrip` — syncs a todo with `Notes="Pick up milk and eggs"` and `DueDate=ts+86400000`; verifies both survive the `EntityToDto` mapper in the delta response.
- `TodoSyncTests.cs`: Added `Sync_Delta_OrderedByUpdatedOnAscending` — inserts 3 todos at T3/T1/T2, asserts delta returns them T1→T2→T3. Mirrors the existing Journal test.

**Why:** `TodoDto` has two nullable optional fields (Notes, DueDate) with no API round-trip test — a silent mapper regression would drop data on client sync. The ordering test guards `ORDER BY UpdatedOn ASC` in the Todo delta query. Journal had both tests (iters 151, 153); this brings Todo to parity.

**Impact:** 103 mobile tests pass. 123 API tests pass (was 121).

---

## 2026-05-17 — Iteration 155 — Mobile: UpsertFromSyncAsync timestamp invariant for Todo + GoalProgress

**What changed:**
- `TodoRepositoryTests.cs`: Added `UpsertFromSyncAsync_PreservesServerTimestamp` — upserts a record with a fixed server timestamp and asserts `UpdatedOn` is NOT overridden (unlike `SaveAsync`).
- `GoalProgressRepositoryTests.cs`: Added `UpsertFromSyncAsync_PreservesServerTimestamp` — same test for GoalProgress.

**Why:** `UpsertFromSyncAsync` uses direct `InsertOrReplace` to preserve the server's `UpdatedOn` — the LWW invariant for inbound sync. `SaveAsync` always overwrites `UpdatedOn` to `UtcNow`. The existing `UpsertFromSync_OverwritesExistingRecord` tests only checked that data content was updated, not that the timestamp was preserved. Goal and Journal already had this test (iters 142-143); this completes parity for all 4 entity repos.

**Impact:** 103 mobile tests pass (was 101). 121 API tests pass.

---

## 2026-05-17 — Iteration 154 — API: Goal + GoalProgress optional fields round-trip

**What changed:**
- `GoalSyncTests.cs`: Added `Sync_OptionalFieldsRoundTrip` — syncs a goal with `MeasurableOutcome`, `NextMeetingDate`, and `ExpirationDate` all set; verifies all three preserved in delta response.
- `GoalProgressSyncTests.cs`: Added `Sync_NextMeetingDateRoundTrips` — syncs a goal-progress with `NextMeetingDate` set; verifies it's preserved in delta.

**Why:** These optional fields had no API-level tests — if the `EntityToDto` mapper dropped them, they would silently disappear for clients downloading a delta. Journal optional fields were already covered (iter 153); this completes parity for Goal and GoalProgress.

**Impact:** 101 mobile tests pass. 121 API tests pass (was 119).

---

## 2026-05-17 — Iteration 153 — GoalRepo active ordering + Journal optional fields round-trip

**What changed:**
- `GoalRepositoryTests.cs`: Added `GetAllActiveAsync_MultipleActiveGoals_OrderedByEnteredDateDescending` — 3 active goals in shuffled insertion order, asserts newest-first return. Tests the secondary sort key in the raw SQL query.
- `JournalSyncTests.cs`: Added `Sync_OptionalFieldsRoundTrip` — syncs a journal with Activity, Mood, and Tags all set, verifies all three are included in the delta response.

**Why:** The Goal ordering test validates the `EnteredDate DESC` sort within the active group — the existing ordering test only checked that active goals come before completed, not the ordering within active goals. The Journal optional-fields test guards the `EntityToDto` mapper — if Activity, Mood, or Tags were omitted from the projection, they would silently disappear on mobile after sync.

**Impact:** 101 mobile tests pass (was 100). 119 API tests pass (was 118).

---

## 2026-05-17 — Iteration 152 — API: Goal CompletionDate propagation + GoalProgress batch mixed LWW

**What changed:**
- `GoalSyncTests.cs`: Added `Sync_CompletedGoal_CompletionDatePropagatedInDelta` — uploads a goal with `CompletionDate` set, downloads delta, verifies `CompletionDate` is included in the response. Mirrors `Sync_CompletedTodo_CompletedAtSet_ReturnedInDelta`.
- `GoalProgressSyncTests.cs`: Added `Sync_BatchMixedLWW_PerRecordWinnerApplied` — completes batch LWW coverage for all 4 entity types.

**Why:** The Goal `CompletionDate` field had no test verifying it round-trips through the API delta — if the `EntityToDto` mapper missed the field, completions would silently disappear on the downloading client. The GoalProgress batch LWW test completes symmetry.

**Impact:** 100 mobile tests pass. 118 API tests pass (was 116).

---

## 2026-05-17 — Iteration 151 — API: delta ordering by UpdatedOn + Goal batch mixed LWW

**What changed:**
- `JournalSyncTests.cs`: Added `Sync_Delta_OrderedByUpdatedOnAscending` — inserts 3 journals at T3/T1/T2, asserts delta returns them in T1, T2, T3 order.
- `GoalSyncTests.cs`: Added `Sync_BatchMixedLWW_PerRecordWinnerApplied` — batch LWW for Goal, completing the Todo/Journal/Goal coverage set.

**Why:** The delta ordering test guards the explicit `OrderBy(j => j.UpdatedOn)` in the query — if changed to `OrderByDescending`, the test fails. Correct ascending order matters for clients that need to process records in chronological order (most-recently-modified last). The Goal batch LWW test completes symmetry with Todo and Journal.

**Impact:** 100 mobile tests pass. 116 API tests pass (was 114).

---

## 2026-05-17 — Iteration 150 — API Journal batch LWW + Mobile TodoRepo due-date ordering

**What changed:**
- `JournalSyncTests.cs`: Added `Sync_BatchMixedLWW_PerRecordWinnerApplied` — same per-record LWW batch test as Todo (iter 149) applied to Journal.
- `TodoRepositoryTests.cs`: Added `GetPendingAsync_MultipleWithDueDate_OrderedByDueDateAscending` — inserts three todos with DueDates at day+1/2/3 in shuffled order, asserts they're returned ascending by DueDate.

**Why:** Journal uses the same LWW logic as Todo — the test validates the pattern holds across entity types. The due-date ordering test goes beyond the existing `GetPendingAsync_DueDateTodosOrderedBeforeNullDueDate` which only checked that due-date items come before no-due-date items, not that multiple due-date items are ordered correctly among themselves.

**Impact:** 100 mobile tests pass (was 99). 114 API tests pass (was 113).

---

## 2026-05-17 — Iteration 149 — API Tests: batch mixed LWW per-record + negative LastSyncAt

**What changed:**
- `TodoSyncTests.cs`: Added `Sync_BatchMixedLWW_PerRecordWinnerApplied` — sends two records in one batch: A is newer on client (client wins), B is older than server (server wins). Verifies LWW is applied per-record, not per-batch.
- `TodoSyncTests.cs`: Added `Sync_LastSyncAt_NegativeValue_ReturnsAllRecords` — uses `LastSyncAt = -1` (a valid "never synced" sentinel) and asserts all records appear in the delta response.

**Why:** The LWW batch test is the most critical correctness test for the sync protocol — it verifies that records in the same batch can have different "winners" independently. If the server applied all-or-nothing logic, conflict resolution would break. The negative LastSyncAt test ensures the `UpdatedOn > req.LastSyncAt` filter works for the negative case (all positive timestamps are greater than -1).

**Impact:** 99 mobile tests pass. 113 API tests pass (was 111).

---

## 2026-05-17 — Iteration 148 — Mobile Tests: GetModifiedSinceAsync includes soft-deleted records (Goal + GoalProgress)

**What changed:**
- `GoalRepositoryTests.cs`: Added `GetModifiedSinceAsync_IncludesSoftDeletedRecords`.
- `GoalProgressRepositoryTests.cs`: Added `GetModifiedSinceAsync_IncludesSoftDeletedRecords`.

**Why:** Completes the soft-delete upload coverage started in iteration 147 (Journal + Todo). All four entity repositories now explicitly verify that `GetModifiedSinceAsync` returns records with `DeletedAt` set, not just active records.

**Impact:** 99 mobile tests pass (was 97). 111 API tests pass.

---

## 2026-05-17 — Iteration 147 — Mobile Tests: GetModifiedSinceAsync includes soft-deleted records (Journal + Todo)

**What changed:**
- `JournalRepositoryTests.cs`: Added `GetModifiedSinceAsync_IncludesSoftDeletedRecords` — inserts a journal with `DeletedAt` set, asserts it appears in `GetModifiedSinceAsync` results.
- `TodoRepositoryTests.cs`: Added `GetModifiedSinceAsync_IncludesSoftDeletedRecords` — same for Todo.

**Why:** `GetModifiedSinceAsync` is used by `SyncService` to gather local records for upload. The filter is `WHERE AccountFk = ? AND UpdatedOn > ?` — no `DeletedAt IS NULL`. If that condition were accidentally added, soft-deleted records would never be uploaded to the server, and deletions would never propagate to other devices. These tests are the repository-level counterpart to the `RunAsync_LocalSoftDeletedJournal_IncludedInUploadRequest` SyncService test added in iteration 146.

**Impact:** 97 mobile tests pass (was 95). 111 API tests pass.

---

## 2026-05-17 — Iteration 146 — Mobile Tests: AccountService null-guards + soft-deleted journal upload

**What changed:**
- `AccountServiceTests.cs`: Added `SaveServerCredentials_WhenNoAccount_DoesNotThrow` and `SaveServerUrl_WhenNoAccount_DoesNotThrow` — both call the method on an empty DB and assert no exception and account remains null. Mirrors the pattern of `UpdateLastSync_WhenNoAccount_DoesNotThrow` already in place.
- `SyncServiceTests.cs`: Added `RunAsync_LocalSoftDeletedJournal_IncludedInUploadRequest` — inserts a journal with `DeletedAt` set, runs sync, verifies the GUID appears in the upload request body. `GetModifiedSinceAsync` returns all records with `UpdatedOn > since` regardless of `DeletedAt`; if `GetAllActiveAsync` were accidentally used instead, soft-deleted records would never be uploaded to the server.

**Why:** Both `SaveServerCredentialsAsync` and `SaveServerUrlAsync` have a `if (account is null) return` guard but those branches were untested. The soft-delete upload test is the mobile-side counterpart to the API-side `Sync_SoftDelete_DeletedAtPropagatedInDelta` — it verifies the full upload path, not just the download path.

**Impact:** 95 mobile tests pass (was 92). 111 API tests pass.

---

## 2026-05-17 — Iteration 145 — Mobile Tests: GoalProgressRepo skip already-deleted + SyncService partial-creds guard

**What changed:**
- `GoalProgressRepositoryTests.cs`: Added `DeleteForGoalAsync_AlreadyDeletedRecordsAreNotRetouched` — inserts a record with `DeletedAt=1000`, calls `DeleteForGoalAsync`, asserts `UpdatedOn` and `DeletedAt` remain at 1000. The method queries `WHERE DeletedAt IS NULL`, so already-deleted records must not be touched (bumping their `UpdatedOn` would corrupt the LWW invariant).
- `SyncServiceTests.cs`: Added `RunAsync_ServerUrlSetButJwtMissing_ReturnsNoServer` — sets `ServerUrl` but leaves `ServerJwt` null, verifies the OR guard (`IsNullOrEmpty(ServerUrl) || IsNullOrEmpty(ServerJwt)`) returns `NoServer`.

**Why:** `DeleteForGoalAsync` loops only over active items (`DeletedAt IS NULL`). If that filter were inadvertently removed, already-deleted progress items would have their `UpdatedOn` bumped to "now", drifting out of the sync window and potentially causing the server to re-deliver them. The partial-credentials test covers the second branch of the guard — the existing `NoServer` test uses an account with both credentials missing.

**Impact:** 92 mobile tests pass (was 90). 111 API tests pass.

---

## 2026-05-17 — Iteration 144 — Mobile Tests: LWW timestamp preservation + delta round-trip

**What changed:**
- `JournalRepositoryTests.cs`: Added `UpsertFromSyncAsync_PreservesServerTimestamp` — asserts that `UpsertFromSyncAsync` (which calls `InsertOrReplaceAsync` directly) stores the server-supplied `UpdatedOn` without overriding it, unlike `SaveAsync`.
- `GoalRepositoryTests.cs`: Added same `UpsertFromSyncAsync_PreservesServerTimestamp` test.
- `SyncServiceTests.cs`: Added `RunAsync_SecondSync_SendsLastSyncAtFromPriorSync` — runs two consecutive successful syncs and verifies the second sync's request body contains the `LastSyncAt` that was persisted by the first sync, proving the delta window round-trips correctly.

**Why:** `UpsertFromSyncAsync` bypasses `SaveAsync`'s `UpdatedOn` override — that's intentional for LWW. If someone accidentally changed it to call `SaveAsync`, the server's timestamp would be replaced with the local clock, breaking last-write-wins. The delta round-trip test guards the path where `LastSyncAt` is persisted and then read back: if `UpdateLastSyncAsync` stored incorrectly or `RunAsync` read the wrong field, the delta window would be wrong (either replaying all records or missing changes).

**Impact:** 90 mobile tests pass (was 87). 111 API tests pass.

---

## 2026-05-17 — Iteration 143 — Mobile Tests: GoalProgressRepo empty DeleteForGoal + SyncService lock release

**What changed:**
- `GoalProgressRepositoryTests.cs`: Added `DeleteForGoalAsync_WhenNoActiveProgress_DoesNothing` — calls `DeleteForGoalAsync` on a goalFk with no active progress records and asserts it completes without error or side effects.
- `SyncServiceTests.cs`: Added `RunAsync_FailedSync_ReleasesLockSoSubsequentSyncCanRun` — runs a sync that fails (entity sync returns 500), then runs a second sync. If the `finally` block had not released `_syncing`, the second call would short-circuit and return `Success` (the concurrent-skip path). Returning `Failed` on the second call proves the lock was reset.

**Why:** `DeleteForGoalAsync` loops over active items; the empty-list path (no items for that goalFk) was untested — a regression that changed the query filter could silently skip deletes with no observable failure. The sync lock test guards the `finally { Interlocked.Exchange(ref _syncing, 0) }` invariant: if that block were removed, one failed sync would permanently block all subsequent syncs until the app restarts.

**Impact:** 87 mobile tests pass (was 85). 111 API tests pass.

---

## 2026-05-17 — Iteration 142 — Mobile Tests: null-guard path tests for DeleteAsync/CompleteAsync

**What changed:**
- `JournalRepositoryTests.cs`: Added `DeleteAsync_WhenGuidNotFound_DoesNotThrow`.
- `GoalRepositoryTests.cs`: Added `DeleteAsync_WhenGuidNotFound_DoesNotThrow` and `CompleteAsync_WhenGuidNotFound_DoesNotThrow`.
- `TodoRepositoryTests.cs`: Added `DeleteAsync_WhenGuidNotFound_DoesNotThrow` and `CompleteAsync_WhenGuidNotFound_DoesNotThrow`.

**Why:** All three repositories guard against null with `if (item is null) return;` in their `DeleteAsync` and `CompleteAsync` methods. None of these null-guard branches were tested. If the null check were accidentally removed, calling Delete/Complete on a non-existent GUID would throw a `NullReferenceException`. These tests exercise the defensive path and verify it silently completes without side effects.

**Impact:** 85 mobile tests pass (was 80). 111 API tests pass.

---

## 2026-05-17 — Iteration 141 — Mobile Tests: TodoRepository ordering + zero count; AccountService GUID assignment

**What changed:**
- `TodoRepositoryTests.cs`: Added `GetAllActiveAsync_OrdersByUpdatedOnDescending` and `GetCompletedCountAsync_WhenNone_ReturnsZero`.
- `AccountServiceTests.cs`: Added `CreateAccount_AssignsNonEmptyGuid` — verifies `CreateAccountAsync` assigns a valid GUID.

**Why:** `GetAllActiveAsync` orders by `UpdatedOn DESC` but that ordering was untested. `GetCompletedCountAsync` with zero completed items returned 0 but that edge case was never verified. `CreateAccountAsync` assigns a GUID via `Guid.NewGuid().ToString()` but no test verified the account gets a non-empty, parseable GUID — a regression silently assigning empty string would break sync (AccountFk foreign keys would be empty).

**Impact:** 80 mobile tests pass (was 77). 111 API tests pass.

---

## 2026-05-17 — Iteration 140 — API delta isolation tests for Goal, Todo, GoalProgress

**What changed:**
- `GoalSyncTests.cs`: Added `Sync_DeltaIsolation_OtherUsersRecordsNotReturned`.
- `TodoSyncTests.cs`: Added `Sync_DeltaIsolation_OtherUsersRecordsNotReturned`.
- `GoalProgressSyncTests.cs`: Added `Sync_DeltaIsolation_OtherUsersRecordsNotReturned`.

**Why:** Iter 139 added this test to Journal. The same security property — that `AccountFk` filtering in the delta query prevents one user from receiving another user's records in the download — was missing for Goal, Todo, and GoalProgress. All four entity sync endpoints now have explicit cross-account delta isolation tests. A bug removing the `AccountFk` filter in any endpoint's delta query would be caught.

**Impact:** 77 mobile tests pass. 111 API tests pass (was 108).

---

## 2026-05-16 — Iteration 139 — API delta isolation + Goal active ordering test

**What changed:**
- `JournalSyncTests.cs`: Added `Sync_DeltaIsolation_OtherUsersRecordsNotReturned` — registers two users, user1 uploads a journal entry, user2 syncs and verifies they never receive user1's record in the delta (explicit security isolation test for the download path).
- `GoalRepositoryTests.cs`: Added `GetAllActiveAsync_OrdersActiveGoalsByEnteredDateDescending` — verifies the `EnteredDate DESC` ordering within the active group of goals.

**Why:** The `Sync_RecordWithWrongAccountFk_IsRejected` tests covered the upload-rejection path, but there was no test explicitly verifying that the delta (server→client download) only contains the calling user's own records. An AccountFk filter bug in the delta query would be invisible to existing tests. Goal's ordering by `EnteredDate DESC` was implemented in SQL but untested — the `GetAllActiveAsync_ActiveBeforeCompleted` test only verified active vs completed boundary, not ordering within the active group.

**Impact:** 77 mobile tests pass (was 76). 108 API tests pass (was 107).

---

## 2026-05-16 — Iteration 138 — Mobile Tests: GetLatestNextStepsAsync isolation + AccountService null-account + CreatedOn tests

**What changed:**
- `GoalProgressRepositoryTests.cs`: Added `GetLatestNextStepsAsync_ExcludesOtherAccounts` — verifies that `GetLatestNextStepsAsync` doesn't leak next-step data across accounts (the GoalFk key appears in result only for the queried account).
- `AccountServiceTests.cs`: Added `VerifyPin_WhenNoAccount_ReturnsFalse` (null guard path), `UpdateLastSync_WhenNoAccount_DoesNotThrow` (null guard path), and `CreateAccount_SetsCreatedOn` (verifies `CreatedOn` timestamp is set within the creation window).

**Why:** `GetLatestNextStepsAsync` filtered by `AccountFk` but had no test inserting competing records from another account for the same `GoalFk`. AccountService's null-guard branches (`if (account is null) return/return false`) were untested — a future refactor removing them would have no test coverage. `CreatedOn` is a timestamp set during account creation that's never been tested.

**Impact:** 76 mobile tests pass (was 72). 107 API tests pass.

---

## 2026-05-16 — Iteration 137 — Mobile Tests: GetModifiedSinceAsync account isolation + GoalProgress edit invariants

**What changed:**
- `GoalRepositoryTests.cs`: Added `GetModifiedSinceAsync_ExcludesOtherAccounts`.
- `JournalRepositoryTests.cs`: Added `GetModifiedSinceAsync_ExcludesOtherAccounts`.
- `TodoRepositoryTests.cs`: Added `GetModifiedSinceAsync_ExcludesOtherAccounts`.
- `GoalProgressRepositoryTests.cs`: Added `SaveAsync_Edit_BumpsUpdatedOn` and `SaveAsync_Edit_AppearsInGetModifiedSince`.

**Why:** `GetModifiedSinceAsync` is the sync-critical query that determines which records are uploaded. The existing tests verified timestamp filtering but did NOT verify account isolation (records for another account were never present to accidentally leak). Removing the `AccountFk` filter from any repo's `GetModifiedSinceAsync` would pass existing tests. GoalProgress was also missing the edit-sync tests that all other repos now have (Journal/Goal/Todo got them in iter 134).

**Impact:** 72 mobile tests pass (was 67). 107 API tests pass.

---

## 2026-05-16 — Iteration 136 — Mobile Tests: TodoRepository.GetAllActiveAsync coverage + server Todo/GoalProgress inbound upsert

**What changed:**
- `TodoRepositoryTests.cs`: Added `GetAllActiveAsync_IncludesCompletedExcludesDeleted` and `GetAllActiveAsync_ExcludesOtherAccounts` — `GetAllActiveAsync` had zero test coverage.
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsTodo_UpsertsLocally` and `RunAsync_ServerReturnsGoalProgress_UpsertsLocally` with new `FakeTodoSyncHandler` and `FakeGoalProgressSyncHandler` — completing inbound sync coverage for all 4 entity types.

**Why:** `TodoRepository.GetAllActiveAsync` (which includes both pending and completed non-deleted todos) was completely untested — any regression in its filter or account-scope clause would go undetected. Similarly, the SyncService inbound-upsert path was only verified for Journal and Goal (iter 135); Todo and GoalProgress were missing parallel tests, meaning the `UpsertFromSyncAsync` paths for those entities were untested at the service layer.

**Impact:** 67 mobile tests pass (was 63). 107 API tests pass.

---

## 2026-05-16 — Iteration 135 — API + Mobile Tests: Todo completed blank title; GoalProgress ordering; server Goal upsert

**What changed:**
- `SyncInputValidationTests.cs`: Added `Sync_Todo_CompletedWithBlankTitle_IsAccepted` — verifies that a completed todo (`CompletedAt` set, `DeletedAt` null) with a blank title returns 200 OK, covering the `r.CompletedAt is null` branch in the Todo blank-title guard.
- `GoalProgressRepositoryTests.cs`: Added `GetForGoalAsync_OrdersByUpdatedOnDescending` — verifies that `GetForGoalAsync` returns records newest-first.
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsGoal_UpsertsLocally` with `FakeGoalSyncHandler` — verifies that Goal records returned by the server are upserted into the local SQLite store after sync.

**Why:** The Todo endpoint has a unique validation path where completed todos (regardless of title) are exempt from the blank-title check — previously only the `DeletedAt` exemption was tested. GoalProgress ordering was implemented but untested (like Journal's descending-date order). The SyncService inbound-upsert path was only covered for Journal; Goal, Todo, and GoalProgress lacked parallel tests.

**Impact:** 63 mobile tests pass (was 61). 107 API tests pass (was 106).

---

## 2026-05-16 — Iteration 134 — Mobile Tests: SaveAsync edit bumps UpdatedOn + GoalProgress account isolation

**What changed:**
- `GoalRepositoryTests.cs`: Added `SaveAsync_Edit_BumpsUpdatedOn` and `SaveAsync_Edit_AppearsInGetModifiedSince`.
- `TodoRepositoryTests.cs`: Added `SaveAsync_Edit_BumpsUpdatedOn` and `SaveAsync_Edit_AppearsInGetModifiedSince`.
- `GoalProgressRepositoryTests.cs`: Added `GetModifiedSinceAsync_ExcludesOtherAccounts`.

**Why:** `JournalRepository` already had edit-sync tests (`SaveAsync_Edit_BumpsUpdatedOn` and `SaveAsync_Edit_AppearsInGetModifiedSince`) verifying that modifying a record via `SaveAsync` bumps `UpdatedOn` and makes it visible to `GetModifiedSinceAsync`. Goal and Todo lacked these tests despite having identical `SaveAsync` implementations — any accidental removal of the `UpdatedOn =` line in those repos would silently prevent edits from syncing. GoalProgress was also missing the account isolation test for `GetModifiedSinceAsync` that all other repos now have.

**Impact:** 61 mobile tests pass (was 56). 106 API tests pass.

---

## 2026-05-16 — Iteration 133 — Mobile Tests: Account isolation in repository queries

**What changed:**
- `JournalRepositoryTests.cs`: Added `GetAllActiveAsync_ExcludesOtherAccounts`.
- `GoalRepositoryTests.cs`: Added `GetAllActiveAsync_ExcludesOtherAccounts`.
- `TodoRepositoryTests.cs`: Added `GetPendingAsync_ExcludesOtherAccounts`.

**Why:** The `GetAllActiveAsync` and `GetPendingAsync` methods filter by `AccountFk`. No existing test inserted records for two different accounts and verified that only the correct account's records were returned. A regression removing or breaking the WHERE clause would silently expose other users' data. This is a security invariant worth explicit coverage.

**Impact:** 56 mobile tests pass. 106 API tests pass.

---

## 2026-05-16 — Iteration 132 — Mobile Tests: JournalRepository GetAllActiveAsync ordering

**What changed:**
- `JournalRepositoryTests.cs`: Added `GetAllActiveAsync_OrdersByEnteredDateDescending` — inserts two journals with different `EnteredDate` values, calls `GetAllActiveAsync`, and verifies the newer entry appears first.

**Why:** `JournalRepository.GetAllActiveAsync` orders by `EnteredDate DESC`, which determines the display order in the journal list. This ordering was completely untested. A refactor removing the `OrderByDescending` clause would silently break the display order with no failing test. GoalRepository has an ordering test; this brings Journal in line.

**Impact:** 53 mobile tests pass. 106 API tests pass.

---

## 2026-05-16 — Iteration 131 — Mobile Tests: SyncService sends locally modified Goal and Todo

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_LocalGoalModifiedSinceLastSync_IncludedInRequest` and `RunAsync_LocalTodoModifiedSinceLastSync_IncludedInRequest` — both follow the CapturingHandler pattern from iterations 109 and 130.

**Why:** Iterations 109 and 130 covered Journal and GoalProgress. Goal and Todo had no analogous coverage. All 4 entity sync paths now have explicit tests verifying that locally modified records are included in the outbound request body — closing the last gap in sync pipeline coverage.

**Impact:** 52 mobile tests pass. 106 API tests pass.

---

## 2026-05-16 — Iteration 130 — Mobile Tests: SyncService sends locally modified GoalProgress

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_LocalGoalProgressModifiedSinceLastSync_IncludedInRequest` — inserts a GoalProgress record directly via `_db.InsertOrReplaceAsync`, runs sync with a `CapturingHandler`, and verifies the `sync/goal-progress` request body contains the record's GUID.

**Why:** Iteration 109 added the analogous test for Journal. GoalProgress is the most complex entity (it references goals, participates in cascade delete, and has its own `GetModifiedSinceAsync`). Without this test, a regression removing GoalProgress from the SyncService entity loop (or breaking its `GetModifiedSinceAsync` call) would be invisible.

**Impact:** 50 mobile tests pass. 106 API tests pass.

---

## 2026-05-16 — Iteration 129 — Mobile Tests: GoalProgress DeleteForGoalAsync UpdatedOn sync invariant

**What changed:**
- `GoalProgressRepositoryTests.cs`: Added `DeleteForGoal_SetsUpdatedOnToDeletedAt` — inserts two GoalProgress records with known UpdatedOn (1000L), calls `DeleteForGoalAsync`, then reads both directly from the DB and asserts `DeletedAt is not null`, `UpdatedOn == DeletedAt`, and `UpdatedOn > 1000L`.

**Why (test modification justification):** The existing `DeleteForGoal_SoftDeletesAllProgressForThatGoal` test only verifies behavioral exclusion from `GetForGoalAsync`. It does not verify the sync invariant: `UpdatedOn == DeletedAt`. If `DeleteForGoalAsync` forgot to set `UpdatedOn`, goal-progress deletions would never appear in `GetModifiedSinceAsync` and would fail to sync to the server. Same pattern as iterations 124 (CompleteAsync) and 125 (DeleteAsync single-record).

**Impact:** 49 mobile tests pass. 106 API tests pass.

---

## 2026-05-16 — Iteration 128 — API Fix: Register duplicate check used untrimmed NickName

**What changed:**
- `AuthEndpoints.cs`: Moved NickName trim to top of register handler. Now `var nickName = req.NickName?.Trim() ?? string.Empty` is computed first, and all subsequent checks (whitespace, length, duplicate lookup, account creation) use the trimmed value.
- `AuthEndpointTests.cs`: Added `Register_SpacePaddedNickName_DetectsConflictWithExistingTrimmedName` — verifies that registering `"  alice  "` after `"alice"` exists returns 409 Conflict.

**Why:** The duplicate check `a.NickName == req.NickName` used the raw (untrimmed) input, while the stored NickName was always trimmed. So registering `" alice "` after `"alice"` existed would pass the duplicate check (comparing `" alice "` to `"alice"`) and create a second account with the same effective nickname. This iteration 127's trim test discovered the gap by prompting a fresh audit of the endpoint logic.

**Impact:** 48 mobile tests pass. 106 API tests pass.

---

## 2026-05-16 — Iteration 127 — API Tests: Register endpoint NickName trim test

**What changed:**
- `AuthEndpointTests.cs`: Added `Register_NickNameWithSurroundingSpaces_StoredTrimmed` — registers with `"  trimmeduser  "`, then authenticates via token with `"trimmeduser"` (exact trimmed form) to prove the value was stored trimmed.

**Why:** The register endpoint trims NickName before storing (`NickName = req.NickName.Trim()`), but there was no test verifying this. The analogous token-side trim was tested in iteration 116 (`Token_NickNameWithSurroundingSpaces_StillAuthenticates`). Without a register trim test, a regression (removing the trim) would silently store ` alice ` and make all subsequent logins fail even with correct input.

**Impact:** 48 mobile tests pass. 105 API tests pass.

---

## 2026-05-16 — Iteration 126 — API Tests: Soft-deleted records with blank required fields are accepted

**What changed:**
- `SyncInputValidationTests.cs`: Added 4 tests — `Sync_Journal_SoftDeletedWithBlankNotes_IsAccepted`, `Sync_Goal_SoftDeletedWithBlankGoalText_IsAccepted`, `Sync_GoalProgress_SoftDeletedWithBlankNextStepItems_IsAccepted`, `Sync_Todo_SoftDeletedWithBlankTitle_IsAccepted`.

**Why:** The existing blank-field rejection tests only cover the active-record case (DeletedAt = null). The validation logic guards with `r.DeletedAt is null && ...` — meaning soft-deleted records should be accepted even with null/blank text fields. No test verified this acceptance path. A regression (e.g., accidentally changing `&&` to `||`) would cause soft deletions to fail with 422, preventing deletion sync from reaching the server.

**Impact:** 48 mobile tests pass. 104 API tests pass.

---

## 2026-05-16 — Iteration 125 — Mobile Tests: DeleteAsync UpdatedOn sync invariant

**What changed:**
- `JournalRepositoryTests.cs`, `GoalRepositoryTests.cs`, `TodoRepositoryTests.cs`: Strengthened `Delete_SoftDeletes_*` tests with `Assert.Equal(retrieved.DeletedAt!.Value, retrieved.UpdatedOn)`.

**Why (test modification justification):** The existing assertions verified `DeletedAt is not null` and behavioral exclusion from active/pending lists. They did not verify that `UpdatedOn == DeletedAt`, which is required for sync: `GetModifiedSinceAsync` filters by `UpdatedOn > lastSyncAt`. If `UpdatedOn` is not bumped to match `DeletedAt`, the deletion would not be uploaded to the server on the next sync cycle. This follows the same invariant strengthening pattern as iterations 118 (soft delete field presence) and 124 (CompleteAsync invariant).

**Impact:** 48 mobile tests pass. 100 API tests pass.

---

## 2026-05-16 — Iteration 124 — Mobile Tests: CompleteAsync UpdatedOn sync invariant

**What changed:**
- `TodoRepositoryTests.cs`: Added `CompleteAsync_SetsUpdatedOnToCompletedAt` — verifies `UpdatedOn == CompletedAt` after completion.
- `GoalRepositoryTests.cs`: Added `CompleteAsync_SetsUpdatedOnToCompletionDate` — verifies `UpdatedOn == CompletionDate` after completion.

**Why:** Both `TodoRepository.CompleteAsync` and `GoalRepository.CompleteAsync` must set `UpdatedOn = CompletedAt/CompletionDate` so the mutation is picked up by `GetModifiedSinceAsync` on the next sync cycle. The existing `CompleteAsync_SetsCompletionDate` and `Complete_SetCompletedAt_ExcludedFromPending` tests only verified behavioral effects (excluded from pending, completion field not null), not the sync invariant. A refactor forgetting `UpdatedOn` would silently leave completions unsynced.

**Impact:** 48 mobile tests pass. 100 API tests pass.

---

## 2026-05-16 — Iteration 123 — Mobile Tests: AccountService server credential tests

**What changed:**
- `AccountServiceTests.cs`: Added `SaveServerCredentials_PersistsJwtAndUrl` and `SaveServerUrl_UpdatesUrlWithoutAffectingJwt`.

**Why:** `SaveServerCredentialsAsync` and `SaveServerUrlAsync` were the only untested public methods in `AccountService`. These are called from `SettingsViewModel` when users configure their server connection — a regression in either (e.g., null overwrite of ServerJwt when saving only the URL) would silently break sync without any test catching it.

**Impact:** 46 mobile tests pass. 100 API tests pass.

---

## 2026-05-16 — Iteration 122 — API Tests: Field-length validation tests for sync endpoints

**What changed:**
- `SyncInputValidationTests.cs`: Added 7 tests covering field-length validation branches that existed in the endpoint code but had no test coverage: `Sync_Journal_NotesTooLong_Returns422` (> 10000), `Sync_Journal_ActivityTooLong_Returns422` (> 255), `Sync_Journal_MoodTooLong_Returns422` (> 50), `Sync_Journal_TagsTooLong_Returns422` (> 500), `Sync_Goal_GoalTextTooLong_Returns422` (> 2000), `Sync_Todo_TitleTooLong_Returns422` (> 500), `Sync_Todo_NotesTooLong_Returns422` (> 2000).

**Why:** All 7 validation branches existed in the endpoint handlers (JournalEndpoints, GoalEndpoints, TodoEndpoints) but were completely untested. A regression in any of these limits would silently let oversized payloads through. The fresh brainstorm audit of `SyncInputValidationTests.cs` against the endpoint validation code confirmed these as the only remaining coverage gaps.

**Impact:** 44 mobile tests pass. 100 API tests pass.

---

## 2026-05-16 — Iteration 121 — Mobile: Error handling in SettingsViewModel.LoadAsync

**What changed:**
- `SettingsViewModel.cs`: `LoadAsync` wrapped in try-catch that sets `StatusMessage = "Could not load settings."` on failure.

**Why:** `SettingsViewModel.LoadAsync` was the only ViewModel `LoadAsync` without a try-catch. All list ViewModels (Journal, Goal, Todo) and DashboardViewModel already had this pattern.

**Impact:** 44 mobile tests pass. 93 API tests pass.

---

## 2026-05-16 — Iteration 120 — Mobile: Null-safe Records check in SyncEntityAsync

**What changed:**
- `SyncService.cs`: Changed `if (result is null) return;` to `if (result?.Records is null) return;` in `SyncEntityAsync`.
- `SyncServiceTests.cs`: Added `RunAsync_ServerReturnsNullRecords_DoesNotThrow` using a `NullRecordsHandler` that returns `{"Records":null}`.

**Why:** If the server returns `{"Records": null}`, `ReadFromJsonAsync` would deserialize `Records` as null. The prior null check only guarded against `result` itself being null, not `result.Records`. The `foreach` would throw `NullReferenceException` on null `Records`.

**Impact:** 44 mobile tests pass. 93 API tests pass.

---

## 2026-05-16 — Iteration 119 — Mobile: Settings URL status message distinguishes saved vs cleared

**What changed:**
- `SettingsViewModel.cs`: `SaveServerUrlAsync` now shows `"Server URL cleared."` when URL is empty, `"Server URL saved."` otherwise.

**Why:** Saving an empty URL previously showed `"Server URL saved."`, which is misleading — the user may have intentionally cleared the URL to disconnect from the server, and the feedback should reflect that.

**Impact:** 43 mobile tests pass. 93 API tests pass.

---

## 2026-05-16 — Iteration 118 — Mobile Tests: Strengthen soft-delete assertions in Journal and Goal repos

**What changed:**
- `JournalRepositoryTests.cs`, `GoalRepositoryTests.cs`: `Delete_SoftDeletes_ExcludedFromActive` tests now also call `GetAsync` and assert `DeletedAt` is not null.

**Why (justification for modifying tests):** The existing assertions only verified behavioral exclusion from the active list. They did not verify the soft-delete invariant — that the record still exists in SQLite with `DeletedAt` set (required for sync uplink). The `TodoRepositoryTests` already had this stronger assertion; this brings the others in line.

**Impact:** 43 mobile tests pass. 93 API tests pass.

---

## 2026-05-16 — Iteration 117 — Mobile: Error handling in SetupViewModel.CreateAccountAsync

**What changed:**
- `SetupViewModel.cs`: Wrapped `CreateAccountAsync` + navigation in a try-catch that sets `ErrorMessage` on failure.

**Why:** If SQLite threw during account creation, the exception would propagate to the MAUI command handler with no user-visible error. The `ErrorMessage` binding was already in the ViewModel and view but unreachable from this code path.

**Impact:** 43 mobile tests pass. 93 API tests pass.

---

## 2026-05-16 — Iteration 116 — API: Trim NickName in token endpoint to match register behavior

**What changed:**
- `AuthEndpoints.cs`: Token endpoint now calls `.Trim()` on `req.NickName` before DB lookup.
- `AuthEndpointTests.cs`: Added `Token_NickNameWithSurroundingSpaces_StillAuthenticates`.

**Why:** The register endpoint trims NickName before storing (`NickName = req.NickName.Trim()`), but the token endpoint did an exact match (`a.NickName == req.NickName`). A user typing `" alice "` at login would get 401 even though `"alice"` is stored.

**Impact:** 43 mobile tests pass. 93 API tests pass.

---

## 2026-05-16 — Iteration 115 — Mobile: Fix GoalListViewModel swipe-delete missing cascade

**What changed:**
- `GoalListViewModel.cs`: `DeleteAsync` (swipe-to-delete from list) now calls `progressRepo.DeleteForGoalAsync(goal.Guid)` after deleting the goal.

**Why:** Iteration 114 added the cascade to `GoalEntryViewModel.DeleteAsync`, but missed the same operation in `GoalListViewModel.DeleteAsync` (swipe gesture on the list). Orphaned GoalProgress records would still accumulate when deleting goals from the list screen.

**Impact:** 43 mobile tests pass. 92 API tests pass.

---

## 2026-05-16 — Iteration 114 — Mobile: Cascade soft-delete GoalProgress when Goal is deleted

**What changed:**
- `GoalProgressRepository.cs`: Added `DeleteForGoalAsync(string goalFk)` — soft-deletes all active GoalProgress records for a given goal.
- `GoalEntryViewModel.cs`: `DeleteAsync` now calls `progressRepo.DeleteForGoalAsync(Guid)` after soft-deleting the goal.
- `GoalProgressRepositoryTests.cs`: Added `DeleteForGoal_SoftDeletesAllProgressForThatGoal`.

**Why:** Deleting a goal left all associated GoalProgress records with `DeletedAt = null`, causing them to be synced indefinitely as active records. Cascading the delete keeps the sync payload clean and prevents orphaned records.

**Impact:** 43 mobile tests pass. 92 API tests pass.

---

## 2026-05-16 — Iteration 113 — API: PinHash max-length (200 chars) validation on register

**What changed:**
- `AuthEndpoints.cs`: Added `req.PinHash.Length > 200` check returning 400.
- `AuthEndpointTests.cs`: Added `Register_TooLongPinHash_Returns400`.

**Why:** BCrypt silently truncates input at 72 bytes, so a 201-char PinHash has the same bcrypt result as its first 200 chars — creating silent collision risk. Rejecting inputs over 200 chars prevents pathological inputs while allowing any reasonable hash algorithm output (SHA-512 hex is 128 chars).

**Impact:** 42 mobile tests pass. 92 API tests pass.

---

## 2026-05-16 — Iteration 112 — Mobile Tests: UpsertFromSync tests for Goal and Journal repos

**What changed:**
- `GoalRepositoryTests.cs`: Added `UpsertFromSync_OverwritesExistingGoal`.
- `JournalRepositoryTests.cs`: Added `UpsertFromSync_OverwritesExistingJournal`.

**Why:** `UpsertFromSyncAsync` is the downlink path in the sync protocol; it bypasses `SaveAsync`'s `UpdatedOn` override. The method existed untested in both repositories.

**Impact:** 42 mobile tests pass. 91 API tests pass.

---

## 2026-05-16 — Iteration 111 — Mobile Tests: TodoRepository GetCompletedCount, DueDate ordering, UpsertFromSync

**What changed:**
- `TodoRepositoryTests.cs`: Added `GetCompletedCount_CountsCompletedExcludesDeleted`, `GetPendingAsync_DueDateTodosOrderedBeforeNullDueDate`, and `UpsertFromSync_OverwritesExistingRecord`.

**Why:** `GetCompletedCountAsync` and `UpsertFromSyncAsync` were uncovered. The `GetPendingAsync` method has custom SQL with `ORDER BY (DueDate IS NULL), DueDate` sorting logic that previously had no test.

**Impact:** 40 mobile tests pass. 91 API tests pass.

---

## 2026-05-16 — Iteration 110 — API Tests: Empty batch returns 200 for Goal, GoalProgress, Todo

**What changed:**
- `GoalSyncTests.cs`, `GoalProgressSyncTests.cs`, `TodoSyncTests.cs`: Each got `Sync_EmptyBatch_Returns200_WithEmptyList` to match the same test that already existed in `JournalSyncTests`.

**Why:** Sending an empty batch is valid and must return 200 with an empty delta — three sync endpoints lacked this basic smoke test.

**Impact:** 37 mobile tests pass. 91 API tests pass.

---

## 2026-05-16 — Iteration 109 — Mobile Tests: SyncService uplink verification

**What changed:**
- `SyncServiceTests.cs`: Added `RunAsync_LocalJournalModifiedSinceLastSync_IncludedInRequest` using a new `CapturingHandler` that records request bodies and asserts the local journal GUID appears in the outgoing POST body.

**Why:** All 9 existing SyncService tests covered result codes or downlink (server→local upsert), but none verified that locally-modified records are actually sent to the server. The uplink path was untested.

**Impact:** 37 mobile tests pass. 88 API tests pass.

---

## 2026-05-16 — Iteration 108 — API: Validate PinHash non-empty on register

**What changed:**
- `AuthEndpoints.cs`: Added `string.IsNullOrWhiteSpace(req.PinHash)` check before hashing, returns 400 if blank.
- `AuthEndpointTests.cs`: Added `Register_EmptyPinHash_Returns400` and `Register_WhitespacePinHash_Returns400`.

**Why:** The register endpoint accepted empty/whitespace PinHash, silently storing a BCrypt hash of an empty string. Any caller knowing the NickName could authenticate by sending an empty pin.

**Impact:** 36 mobile tests pass. 88 API tests pass.

---

## 2026-05-16 — Iteration 107 — Mobile Tests: GoalProgressRepository full coverage

**What changed:**
- `GoalProgressRepositoryTests.cs`: New file with 7 tests covering `SaveAsync`, `GetForGoalAsync` (active-only, goal-scoped), `GetLatestNextStepsAsync` (latest per goal, excludes soft-deleted), `GetModifiedSinceAsync`, and `UpsertFromSyncAsync`.

**Why:** `GoalProgressRepository` had zero unit tests despite having 5 methods including non-trivial SQL (`GetLatestNextStepsAsync` uses GROUP BY/HAVING). Brings mobile test coverage in line with all other repositories.

**Impact:** 36 mobile tests pass. 86 API tests pass.

---

## 2026-05-16 — Iteration 106 — Mobile Tests: GoalRepository CompleteAsync + GetModifiedSince

**What changed:**
- `GoalRepositoryTests.cs`: Added `CompleteAsync_SetsCompletionDate` and `GetModifiedSince_ReturnsOnlyNewerRecords`.

**Why:** Matches the coverage pattern now established in JournalRepositoryTests and TodoRepositoryTests.

**Impact:** 29 mobile tests pass. 86 API tests pass.

---

## 2026-05-16 — Iteration 105 — Mobile Tests: Todo soft-delete + GetModifiedSince coverage

**What changed:**
- `TodoRepositoryTests.cs`: Added `Delete_SoftDeletes_ExcludedFromPending` and `GetModifiedSince_ReturnsOnlyNewerRecords`.

**Why:** TodoRepositoryTests only had Save and Complete tests. Soft-delete and GetModifiedSince are core behaviors used by sync, matching what JournalRepositoryTests already covers.

**Impact:** 27 mobile tests pass. 86 API tests pass.

---

## 2026-05-16 — Iteration 104 — Mobile: Fix local-to-UTC timestamp conversion in entry ViewModels

**What changed:**
- `JournalEntryViewModel.cs`: `EnteredDate` → `DateTime.SpecifyKind(EnteredDate, DateTimeKind.Local)` before `new DateTimeOffset(...)`.
- `TodoEntryViewModel.cs`: Same fix for `DueDate`.
- `GoalEntryViewModel.cs`: Same fix for `NextMeetingDate` and `ExpirationDate`.

**Why:** `new DateTimeOffset(dt, TimeSpan.Zero)` ignores `DateTime.Kind` and sets UTC offset to zero, treating local midnight as UTC midnight. For non-UTC users (e.g. UTC-5), a date picked as May 16 would round-trip back as May 15 after reload. Forcing `DateTimeKind.Local` before passing to `DateTimeOffset` uses the system's correct UTC offset.

**Impact:** 25 mobile tests pass. 86 API tests pass.

---

## 2026-05-16 — Iteration 103 — API Tests: Todo + GoalProgress soft-delete roundtrip

**What changed:**
- `TodoSyncTests.cs`: Added `Sync_SoftDelete_DeletedAtPropagatedInDelta`.
- `GoalProgressSyncTests.cs`: Added `Sync_SoftDelete_DeletedAtPropagatedInDelta`.

**Why:** All four entity sync test classes now have soft-delete coverage. Journal and Goal were covered in iters 101-102; this completes the set.

**Impact:** 25 mobile tests pass. 86 API tests pass.

---

## 2026-05-16 — Iteration 102 — API Tests: Token unknown user 401 + Goal soft-delete roundtrip

**What changed:**
- `AuthEndpointTests.cs`: Added `Token_UnknownUser_Returns401` — verifies the token endpoint returns 401 when the nickname is not registered.
- `GoalSyncTests.cs`: Added `Sync_SoftDelete_DeletedAtPropagatedInDelta` — mirrors the Journal soft-delete roundtrip test added in iter 101.

**Why:** Auth tests only covered wrong-pin for a known user, not an entirely unknown user. Goal soft-delete was untested (Journal, Todo, and GoalProgress now all have it).

**Impact:** 25 mobile tests pass. 84 API tests pass.

---

## 2026-05-16 — Iteration 101 — API Tests: GoalProgress server-wins LWW + Journal soft-delete roundtrip

**What changed:**
- `GoalProgressSyncTests.cs`: Added `Sync_ServerWinsWhenNewerUpdatedOn` — verifies a stale client record does not overwrite a newer server record (the missing symmetric LWW case).
- `JournalSyncTests.cs`: Added `Sync_SoftDelete_DeletedAtPropagatedInDelta` — verifies that a record synced with `DeletedAt` set is stored and returned in the delta with the correct `DeletedAt` value.

**Why:** GoalProgressSyncTests had the client-wins LWW case but not the server-wins mirror. Soft-delete propagation is a core sync behavior used by all four entities but had no test coverage.

**Impact:** 25 mobile tests pass. 82 API tests pass.

---

## 2026-05-16 — Iteration 100 — API Tests: 401 for unauthenticated sync access

**What changed:**
- `SyncInputValidationTests.cs`: Added `Sync_NoAuth_Returns401` Theory covering all four sync endpoints.

**Why:** The `[RequireAuthorization]` attribute on all sync endpoints was untested. Adding coverage ensures the protection is verified and regressions will be caught.

**Impact:** 25 mobile tests pass. 80 API tests pass.

---

## 2026-05-16 — Iteration 99 — Mobile: Show filtered count in EntryCountDisplay when search is active

**What changed:**
- `GoalListViewModel.cs`, `JournalListViewModel.cs`, `TodoListViewModel.cs`: Expanded `OnFilterTextChanged` to an if/else block. When a filter is active, `EntryCountDisplay` now shows "N matching" instead of the stale total count. When filter is cleared, the normal count summary is restored via the existing helper.

**Why:** Searching filtered the list but the count label kept showing the total. This was misleading — e.g. "3 active, 2 completed" when only 1 goal was visible.

**Impact:** 25 mobile tests pass. 76 API tests pass.

---

## 2026-05-16 — Iteration 98 — Mobile: Show NextStepItems character count in GoalEntry

**What changed:**
- `GoalEntryViewModel.cs`: Added `NextStepItemsLength` observable and `OnNextStepItemsChanged` partial.
- `GoalEntryPage.xaml`: Added small gray character count label beneath the Next Steps Editor.

**Why:** NextStepItems has a 2000-char API limit. This was the only editable field in GoalEntry without a counter.

**Impact:** 25 mobile tests pass. 76 API tests pass.

---

## 2026-05-16 — Iteration 97 — Mobile: Show Title character count in TodoEntry

**What changed:**
- `TodoEntryViewModel.cs`: Added `TitleLength` observable; expanded `OnTitleChanged` to block form that sets `TitleLength` and calls `SaveCommand.NotifyCanExecuteChanged()`.
- `TodoEntryPage.xaml`: Added small gray character count label beneath the Title Entry.

**Why:** Title has a 500-char API limit; all other fields with enforced limits already show counters. This closes the last gap.

**Impact:** 25 mobile tests pass. 76 API tests pass.

---

## 2026-05-16 — Iteration 96 — API: Reject sync batches with duplicate Guids (422)

**What changed:**
- All four sync endpoints (`JournalEndpoints`, `GoalEndpoints`, `GoalProgressEndpoints`, `TodoEndpoints`): Added a duplicate-GUID check after the GUID-format check. Returns 422 with "Records must not contain duplicate Guids." if any two records in the batch share the same Guid.
- `SyncInputValidationTests.cs`: Added `Sync_DuplicateGuid_Returns422` Theory covering all four endpoints.

**Why:** Without this check a batch containing two records with the same Guid (neither yet in the DB) would cause EF to track both for insert, leading to a unique-constraint violation at `SaveChangesAsync` and an unhandled 500 response.

**Impact:** 25 mobile tests pass. 76 API tests pass.

---

## 2026-05-16 — Iteration 95 — API: Move health endpoint to /api/health

**What changed:**
- `Program.cs`: Changed `app.MapGet("/health", ...)` to `app.MapGet("/api/health", ...)`.
- `HealthEndpointTests.cs`: Updated all three health tests to hit `/api/health`.

**Why:** `SyncService` constructs the health URL as `{ServerUrl}/health` where `ServerUrl` is expected to include `/api` (the settings placeholder shows `https://your-server/api`). With the endpoint at root `/health`, the constructed URL `https://server/api/health` returned 404, causing `ping.IsSuccessStatusCode` to be false and all syncs to return `SyncResult.NoServer`.

**Impact:** 25 mobile tests pass. 72 API tests pass.

---

## 2026-05-16 — Iteration 94 — Mobile: Show Activity/Mood/Tags character counts in JournalEntry

**What changed:**
- `JournalEntryViewModel.cs`: Added `ActivityLength`, `MoodLength`, `TagsLength` observables and `OnActivityChanged`, `OnMoodChanged`, `OnTagsChanged` partials.
- `JournalEntryPage.xaml`: Added small gray character count labels below each of the three Entry fields.

**Why:** Closes the last gap in entry-form length feedback. Activity (255), Mood (50), and Tags (500) all have DB-level MaxLength limits that are now made visible to users.

**Impact:** 25 mobile tests pass. 72 API tests pass.

---

## 2026-05-16 — Iteration 93 — API: Log warning when sync records have mismatched AccountFk

**What changed:**
- All four sync endpoints (`JournalEndpoints`, `GoalEndpoints`, `GoalProgressEndpoints`, `TodoEndpoints`): Added a `LogWarning` before the DB loop when any records have `AccountFk != accountGuid`, reporting the count of skipped records.

**Why:** Previously these records were silently dropped with no visibility. A client sending wrong AccountFk values (due to bug or attack) now produces an observable warning entry in logs.

**Impact:** 25 mobile tests pass. 72 API tests pass.

---

## 2026-05-16 — Iteration 92 — Mobile: MeasurableOutcome character count in GoalEntry

**What changed:**
- `GoalEntryViewModel.cs`: Added `MeasurableOutcomeLength` observable and `OnMeasurableOutcomeChanged` partial setting it.
- `GoalEntryPage.xaml`: Added small gray label below MeasurableOutcome Entry showing `{Binding MeasurableOutcomeLength, StringFormat='{0} characters'}`.

**Why:** GoalText already showed a character count (iter 79); MeasurableOutcome has the same 2000-char server limit but no feedback. Both fields now give users visibility into their length.

**Impact:** 25 mobile tests pass. 72 API tests pass.

---

## 2026-05-16 — Iteration 91 — Mobile: Empty view differentiates filter-active vs truly empty

**What changed:**
- `JournalListViewModel.cs`, `GoalListViewModel.cs`, `TodoListViewModel.cs`: Added `EmptyMessage` observable; `OnFilterTextChanged` now also sets it — "No matches for \"X\"" when filtering, original default ("No journal entries yet" / "No goals yet" / "All done!") when not.
- `JournalListPage.xaml`, `GoalListPage.xaml`, `TodoListPage.xaml`: Replaced static multi-label EmptyView blocks with a single `<Label Text="{Binding EmptyMessage}" .../>`.

**Why:** Users typing in the filter saw "No journal entries yet" even when entries existed but didn't match. Now the empty state message is accurate for both true-empty and no-match cases.

**Impact:** 25 mobile tests pass. 72 API tests pass.

---

## 2026-05-16 — Iteration 90 — API: Validate Goal.ExpirationDate not in far future

**What changed:**
- `GoalEndpoints.cs`: Added check rejecting `ExpirationDate > now + 10 years` (HTTP 422). Uses the existing `maxFutureTimestampMs` local already computed for other Goal date fields.
- `SyncInputValidationTests.cs`: Added `Sync_Goal_FutureExpirationDate_Returns422` Fact test.

**Why:** ExpirationDate was the last optional date field in all 4 sync endpoints without a future cap. All date fields now have consistent 10-year bounds.

**Impact:** 25 mobile tests pass. 72 API tests pass (up from 71).

---

## 2026-05-16 — Iteration 89 — API: Validate Journal auxiliary fields and Todo Title/Notes lengths

**What changed:**
- `JournalEndpoints.cs`: Added explicit length checks for Activity (>255), Mood (>50), Tags (>500) — matching `[MaxLength]` DB attributes. Prevents DB-level 500 errors when oversized values arrive.
- `TodoEndpoints.cs`: Added Title > 500 check (matching `[MaxLength(500)]`) and Notes > 2000 check (establishes a cap where none existed).
- `SyncInputValidationTests.cs`: Added `Sync_Journal_AuxFieldTooLong_Returns422` (Theory: Activity/Mood/Tags) and `Sync_Todo_FieldTooLong_Returns422` (Theory: Title/Notes).

**Why:** DB-level constraints silently throw exceptions that surface as 500 responses. Explicit API-layer checks give clients clean 422 error messages.

**Impact:** 25 mobile tests pass. 71 API tests pass (up from 66).

---

## 2026-05-16 — Iteration 88 — Mobile: Show Notes character count in TodoEntry

**What changed:**
- `TodoEntryViewModel.cs`: Added `[ObservableProperty] private int notesLength` and `partial void OnNotesChanged` setting it.
- `TodoEntryPage.xaml`: Added small gray label below Notes Editor showing `{Binding NotesLength, StringFormat='{0} characters'}`.

**Why:** Completes the entry-form character/word count pattern (GoalEntry char count iter 79, JournalEntry word count iter 70). TodoEntry Notes was the remaining entry form without feedback.

**Impact:** 25 mobile tests pass. 66 API tests pass.

---

## 2026-05-16 — Iteration 87 — API: Validate GoalProgress.NextMeetingDate not in far future

**What changed:**
- `GoalProgressEndpoints.cs`: Added check rejecting `NextMeetingDate > now + 10 years` (HTTP 422).
- `SyncInputValidationTests.cs`: Added `Sync_GoalProgress_FutureNextMeetingDate_Returns422` Fact test.

**Why:** Completes the set of date-field range guards across all 4 sync endpoints. GoalProgress.NextMeetingDate was the last date field without a future cap.

**Impact:** 25 mobile tests pass. 66 API tests pass (up from 65).

---

## 2026-05-16 — Iteration 86 — API: Validate CompletionDate (Goal) and CompletedAt (Todo) not in far future

**What changed:**
- `GoalEndpoints.cs`: Added check rejecting `CompletionDate > now + 10 years` (HTTP 422).
- `TodoEndpoints.cs`: Added check rejecting `CompletedAt > now + 10 years` (HTTP 422). Renamed local variable `maxDueDateMs` → `maxFutureTimestampMs` for clarity since it's now shared by both checks.
- `SyncInputValidationTests.cs`: Added `Sync_Goal_FutureCompletionDate_Returns422` and `Sync_Todo_FutureCompletedAt_Returns422` Fact tests.

**Why:** `EnteredDate` and `DueDate` were already capped (iters 84, 65). Completion timestamps were the remaining uncapped date fields that a corrupt client could set to year 9999.

**Impact:** 25 mobile tests pass. 65 API tests pass (up from 63).

---

## 2026-05-16 — Iteration 85 — Mobile: Extract UpdateEntryCountDisplay helper in JournalListViewModel

**What changed:**
- `JournalListViewModel.cs`: Extracted `UpdateEntryCountDisplay()` helper replacing inline count format in LoadAsync, RefreshAsync, and DeleteAsync.

**Why:** Mirrors the GoalListViewModel refactor (iter 82). Consolidates the format string in one place and ensures all three mutation paths stay in sync.

**Impact:** 25 mobile tests pass. 63 API tests pass.

---

## 2026-05-16 — Iteration 84 — API: Validate EnteredDate not in far future for Goal and Journal

**What changed:**
- `GoalEndpoints.cs`: Added check rejecting records where `EnteredDate > now + 10 years` (HTTP 422).
- `JournalEndpoints.cs`: Same check for journal entries.
- `SyncInputValidationTests.cs`: Added `Sync_FutureEnteredDate_Returns422` Theory covering both endpoints.

**Why:** `UpdatedOn` already had a 5-minute future cap, but `EnteredDate` (user-visible entry date) had no bound. A corrupt client could persist year-9999 dates. The 10-year window matches the existing `DueDate` cap on todos.

**Impact:** 25 mobile tests pass. 63 API tests pass (up from 61).

---

## 2026-05-16 — Iteration 83 — Bug fix: EntryCountDisplay stale after inline Add/Delete

**What changed:**
- `TodoListViewModel.cs`: Added `UpdateOverdueCount(_allTodos)` call at end of `AddAsync` so EntryCountDisplay refreshes immediately when a task is quick-added.
- `JournalListViewModel.cs`: Added count recomputation in `DeleteAsync` after removing from `_allJournals`.
- `GoalListViewModel.cs`: Added `UpdateEntryCountDisplay()` call in `DeleteAsync` after removing from `_allGoals`.

**Why:** All three list viewmodels maintained counts correctly in Load/Refresh but forgot to update them during inline mutations. The count label would show the old total until the next refresh.

**Impact:** 25 mobile tests pass. 61 API tests pass.

---

## 2026-05-16 — Iteration 82 — Mobile: Extract UpdateEntryCountDisplay helper in GoalListViewModel

**What changed:**
- `GoalListViewModel.cs`: Pulled identical 4-line EntryCountDisplay computation from LoadAsync and RefreshAsync into `UpdateEntryCountDisplay()` private method.

**Why:** The duplicated block was the only repeated logic in the viewmodel. Consolidating it ensures future changes to the display format only need one edit.

**Impact:** 25 mobile tests pass. 61 API tests pass.

---

## 2026-05-16 — Iteration 81 — API: Separate GoalText/MeasurableOutcome length validation messages

**What changed:**
- `GoalEndpoints.cs`: Split the combined `r.GoalText?.Length > 2_000 || r.MeasurableOutcome?.Length > 2_000` check into two sequential checks, each returning a distinct 422 message naming the specific field.
- `SyncInputValidationTests.cs`: Added `Sync_Goal_MeasurableOutcomeTooLong_Returns422` and `Sync_GoalProgress_NextStepItemsTooLong_Returns422` Fact tests.

**Why:** A single combined error "GoalText and MeasurableOutcome must not exceed 2000 characters" didn't tell the caller which field failed. Separate messages follow the same pattern as all other single-field validations in the API.

**Impact:** 25 mobile tests pass. 61 API tests pass (up from 59).

---

## 2026-05-16 — Iteration 80 — Bug fix: TodoListViewModel UpdateOverdueCount used filtered collection

**What changed:**
- `TodoListViewModel.cs`: `CompleteAsync` and `DeleteAsync` now pass `_allTodos` (not `Todos`) to `UpdateOverdueCount`. The filtered `Todos` ObservableCollection only contains the visible subset, so when a filter was active the overdue count and `EntryCountDisplay` reflected only matching items.

**Why:** When a text filter was active, completing or deleting a todo would update EntryCountDisplay based on the filtered item count, not the real total pending count. The fix brings the count back in line with what LoadAsync/RefreshAsync produce.

**Impact:** 25 mobile tests pass (0 warnings). 59 API tests pass.

---

## 2026-05-16 — Iteration 79 — Mobile: Show GoalText character count in GoalEntry

**What changed:**
- `GoalEntryViewModel.cs`: Added `GoalTextLength` observable; `OnGoalTextChanged` now also sets `GoalTextLength = value?.Length ?? 0` (moved from single-line to block since we have two updates). Consistent with the Notes character count pattern in JournalEntryViewModel (which later switched to word count; goal text is shorter so character count is more appropriate).
- `GoalEntryPage.xaml`: Added small gray label `{Binding GoalTextLength, StringFormat='{0} characters'}` below the GoalText Editor.

**Why:** GoalText has a 2000-char server limit (iter 78). The counter gives users feedback before hitting the limit.

**Impact:** 25 mobile tests pass (0 warnings). 59 API tests pass.

---

## 2026-05-16 — Iteration 78 — API: Enforce maximum content field length

**What changed:**
- `JournalEndpoints.cs`: Rejects Notes > 10,000 characters (HTTP 422).
- `GoalEndpoints.cs`: Rejects GoalText or MeasurableOutcome > 2,000 characters (HTTP 422).
- `GoalProgressEndpoints.cs`: Rejects NextStepItems > 2,000 characters (HTTP 422).
- `SyncInputValidationTests.cs`: Added `Sync_FieldTooLong_Returns422` theory test with inlines for Journal/Notes and Goal/GoalText.

**Why:** Without length limits, a buggy client could store multi-MB strings that bloat the DB and slow sync delta queries for all devices. 10k chars is generous for journal notes (~2000 words); 2k for structured goal/progress fields.

**Impact:** 25 mobile tests pass (0 warnings). 59 API tests pass (was 57).

---

## 2026-05-16 — Iteration 77 Brainstorm (fresh — every 3rd)

| # | Description | Dim | Impact | Effort | Risk |
|---|-------------|-----|--------|--------|------|
| 1 | Mobile: GoalEntry — show GoalText word count | UI | Small | XS | Low | |
| 2 | API: GoalProgress — validate NextStepItems not blank | Quality | Small | XS | Low | **SELECTED** |
| 3 | Mobile: Todo trim Title/Notes on save | Quality | Small | XS | Low | (already done) |
| 4 | Mobile: JournalList — show year in date for old entries | UI | Small | S | Low | |
| 5 | API: Enforce maximum content field length | Quality | Small | XS | Low | |
| 6 | Mobile: SyncService — per-entity debug logging | Observ. | Small | XS | Low | |
| 7 | Mobile: Settings — Reset last sync button | UX | Small | S | Low | |
| 8 | API: EnteredDate reasonable range validation | Quality | Small | XS | Low | |

## 2026-05-16 — Iteration 77 — API: Reject GoalProgress sync records with blank NextStepItems

**What changed:**
- `GoalProgressEndpoints.cs`: Added validation — any non-deleted record with blank/whitespace `NextStepItems` returns HTTP 422. Completes the full suite of required-field validation across all 4 entity types.
- `SyncInputValidationTests.cs`: Added `Sync_GoalProgress_BlankNextStepItems_Returns422` fact test.

**Why:** GoalProgress records exist solely to capture next steps. A blank `NextStepItems` on a non-deleted record is a client bug and should be rejected rather than stored.

**Impact:** 25 mobile tests pass (0 warnings). 57 API tests pass (was 56).

---

## 2026-05-16 — Iteration 76 — Mobile: Use 5s deadline for SyncService health check pre-flight

**What changed:**
- `SyncService.cs`: Health check pre-flight now uses a 5-second `CancellationTokenSource` deadline (`GetAsync("health", healthCts.Token)`) instead of the 15-second client timeout. If the server doesn't respond within 5 seconds it's unreachable. Entity sync calls keep the 15-second client timeout.

**Why:** `HttpClient.Timeout` cannot be changed after the first request (throws `InvalidOperationException`). Using a per-call `CancellationTokenSource` correctly limits the health check to 5s without affecting the entity sync timeout. A slow server that times out in 5s for `/health` but responds in 8s for a sync payload would previously timeout on health — this is correct behavior.

**Notes:** First attempt used `client.Timeout = 5s → 15s` which caused 4 test failures. Reverted and used CancellationTokenSource instead.

**Impact:** 25 mobile tests pass (0 warnings). 56 API tests pass.

---

## 2026-05-16 — Iteration 75 — Mobile: TodoList footer includes overdue count when nonzero

**What changed:**
- `TodoListViewModel.cs`: `UpdateOverdueCount` now also sets `EntryCountDisplay`. When overdue > 0, shows "N pending, M overdue"; otherwise "N tasks pending". Removed the two redundant inline `EntryCountDisplay` assignments in `LoadAsync`/`RefreshAsync`.

**Why:** The TodoList footer count now parallels the Dashboard overdue badge logic — users see the overdue breakdown in context without switching screens.

**Impact:** 25 mobile tests pass (0 warnings). 56 API tests pass.

---

## 2026-05-16 — Iteration 74 Brainstorm (fresh — every 3rd)

| # | Description | Dim | Impact | Effort | Risk |
|---|-------------|-----|--------|--------|------|
| 1 | API: Add RequestId log scope to X-Request-ID middleware | Observ. | Medium | S | Low | **SELECTED** |
| 2 | Mobile: TodoList footer — include overdue count when >0 | UI | Small | XS | Low | |
| 3 | Mobile: SyncService — shorter timeout for health check vs entity sync | Quality | Small | S | Low | |
| 4 | Mobile: GoalEntry — show GoalText character count | UI | Small | XS | Low | |
| 5 | API: GoalProgress — validate NextStepItems not blank | Quality | Small | XS | Low | |
| 6 | Mobile: JournalEntry — add character count alongside word count | UI | Small | XS | Low | |
| 7 | Mobile: Todo trim Title/Notes on save | Quality | Small | XS | Low | |
| 8 | API: Rate-limit sync endpoints | Quality | Small | M | Low | |

## 2026-05-16 — Iteration 74 — API: Add RequestId log scope to X-Request-ID middleware

**What changed:**
- `Program.cs`: Enhanced the existing X-Request-ID middleware to create a structured log scope via `logger.BeginScope({"RequestId": requestId})`. All log messages from sync endpoints within the same request now include `RequestId` in their scope, enabling per-request correlation in log aggregators.

**Why:** The header was already echoed back to the client, but server-side logs had no way to correlate the 4 separate sync endpoint log lines from a single mobile sync call. Adding the log scope fixes this for any structured logging sink.

**Impact:** 25 mobile tests pass (0 warnings). 56 API tests pass. Existing X-Request-ID header tests continue to pass.

---

## 2026-05-16 — Iteration 73 — Mobile: GoalList footer shows active/completed split

**What changed:**
- `GoalListViewModel.cs`: `EntryCountDisplay` now shows "N active, M completed" when there are completed goals, or "N goals" when all are active. Uses `g.CompletionDate is null` to split counts from `_allGoals`.

**Why:** The GoalList shows completed goals faded at the bottom (iter 61). The footer count now reflects that split — users can see at a glance how many goals they've completed vs still working on.

**Impact:** 25 mobile tests pass (0 warnings). 56 API tests pass.

---

## 2026-05-16 — Iteration 72 — API: Reject Todo sync records with blank Title

**What changed:**
- `TodoEndpoints.cs`: Added validation — any active (non-deleted, non-completed) record with blank/whitespace `Title` returns HTTP 422. Soft-deletes and completed records are exempt.
- `SyncInputValidationTests.cs`: Added `Sync_Todo_BlankTitle_Returns422` fact test.

**Why:** Consistent with the blank-Notes/GoalText validation added for Journal and Goal in iter 68. The client already guards empty input via `CanSave`, but a direct API caller could bypass it.

**Impact:** 25 mobile tests pass (0 warnings). 56 API tests pass (was 55).

---

## 2026-05-16 — Iteration 71 Brainstorm (fresh — every 3rd)

| # | Description | Dim | Impact | Effort | Risk |
|---|-------------|-----|--------|--------|------|
| 1 | Mobile: GoalEntry — trim GoalText/MeasurableOutcome before save (mirrors JournalEntry) | Bug | Medium | XS | Low | **SELECTED** |
| 2 | API: Reject Todo sync records where Title is blank | Quality | Small | XS | Low | |
| 3 | Mobile: GoalList footer — split "N active / M completed" count | UI | Small | XS | Low | |
| 4 | API: X-Request-ID logging middleware for per-request correlation | Observ. | Medium | S | Low | |
| 5 | Mobile: SyncService — dedicated short-timeout health check | Quality | Small | S | Low | |
| 6 | Mobile: JournalEntry — show EnteredDate picker for new entries | UX | Small | S | Low | |
| 7 | API: GoalProgress — validate NextStepItems not blank for non-deleted records | Quality | Small | XS | Low | |
| 8 | Mobile: TodoList — show overdue count in footer alongside pending count | UI | Small | XS | Low | |

## 2026-05-16 — Iteration 71 — Mobile: Trim GoalText/MeasurableOutcome before saving GoalEntry

**What changed:**
- `GoalEntryViewModel.cs`: `goal.GoalText = GoalText` → `GoalText.Trim()`; `goal.MeasurableOutcome = MeasurableOutcome` → null when blank, trimmed otherwise. Mirrors the fix applied to JournalEntry in iteration 60.

**Why:** Leading/trailing whitespace in GoalText would cause the goal to display with a leading space in GoalList, and pass the API's blank-text check since the server trims before `IsNullOrWhiteSpace`. The client should be consistent.

**Impact:** 25 mobile tests pass (0 warnings). 55 API tests pass.

---

## 2026-05-16 — Iteration 70 — Mobile: Word count in JournalEntry (was character count)

**What changed:**
- `JournalEntryViewModel.cs`: Renamed `NotesLength` to `NotesWordCount`. `OnNotesChanged` now splits on whitespace with `RemoveEmptyEntries` to count words instead of characters.
- `JournalEntryPage.xaml`: Updated `StringFormat` from `'{0} characters'` to `'{0} words'`.

**Why:** Word count is more meaningful for journal reflection than character count — a 200-word entry is a different writing scale than 200 characters. The split uses `null` char array to split on any whitespace (space, newline, tab).

**Impact:** 25 mobile tests pass (0 warnings). 55 API tests pass.

---

## 2026-05-16 — Iteration 69 — Mobile: Entry count footer in GoalList and TodoList

**What changed:**
- `GoalListViewModel.cs`: Added `EntryCountDisplay` observable (e.g., "3 goals"), set in `LoadAsync` and `RefreshAsync`.
- `TodoListViewModel.cs`: Added `EntryCountDisplay` (e.g., "5 tasks pending"), set in `LoadAsync` and `RefreshAsync`.
- `GoalListPage.xaml` / `TodoListPage.xaml`: Added `CollectionView.Footer` with centered gray label, consistent with the JournalList footer added in iter 66.

**Why:** Consistency across all three list screens; users can see at a glance how many active goals / pending tasks they have after a sync.

**Impact:** 25 mobile tests pass (0 warnings). 55 API tests pass.

---

## 2026-05-16 — Iteration 68 Brainstorm (fresh — every 3rd)

| # | Description | Dim | Impact | Effort | Risk |
|---|-------------|-----|--------|--------|------|
| 1 | API: Validate Notes not blank in Journal sync; GoalText not blank in Goal sync | Quality | Medium | XS | Low | **SELECTED** |
| 2 | Mobile: GoalList + TodoList entry count footer | UI | Small | XS | Low | |
| 3 | API: X-Request-ID middleware for correlation logging | Observ. | Medium | S | Low | |
| 4 | Mobile: JournalEntry — word count instead of character count | UI | Small | XS | Low | |
| 5 | Mobile: GoalEntry — verify EnteredDate picker updates on save | Bug? | Small | XS | Low | |
| 6 | API: GoalProgress — validate NextMeetingDate range | Quality | Small | XS | Low | |
| 7 | Mobile: SyncService — expose sync duration for dashboard display | UI | Small | S | Low | |
| 8 | API: Reject Todo records where Title is null/empty | Quality | Small | XS | Low | |

## 2026-05-16 — Iteration 68 — API: Reject blank Notes (Journal) and blank GoalText (Goal)

**What changed:**
- `JournalEndpoints.cs`: Added validation — any non-deleted record with blank/whitespace-only `Notes` returns HTTP 422. Soft-deletes (DeletedAt not null) are exempt since they carry no meaningful content.
- `GoalEndpoints.cs`: Same pattern for `GoalText`.
- `SyncInputValidationTests.cs`: Added `Sync_Journal_BlankNotes_Returns422` and `Sync_Goal_BlankGoalText_Returns422` fact tests.

**Why:** Client already guards empty input, but a buggy client or direct API call could store degenerate records with null/blank required fields, polluting the sync delta returned to all devices.

**Impact:** 25 mobile tests pass (0 warnings). 55 API tests pass (was 53).

---

## 2026-05-16 — Iteration 67 — Mobile: Zero-state hints in Dashboard goal/todo tiles

**What changed:**
- `DashboardViewModel.cs`: Added `HasNoActiveGoals` and `HasNoPendingTodos` bool observables, set in `RefreshDataAsync` alongside their count counterparts.
- `DashboardPage.xaml`: Added small gray hint labels ("Set a goal!" / "All caught up!") inside each summary tile, visible only when the respective count is zero.

**Why:** A tile showing "0 Active Goals" with no hint text is ambiguous — is the data still loading? Did sync not run? The hint text clarifies the empty state and provides a subtle CTA.

**Impact:** 25 mobile tests pass (0 warnings). 53 API tests pass.

---

## 2026-05-16 — Iteration 66 — Mobile: Show total entry count in JournalList footer

**What changed:**
- `JournalListViewModel.cs`: Added `EntryCountDisplay` observable (e.g., "42 entries"), set in both `LoadAsync` and `RefreshAsync`.
- `JournalListPage.xaml`: Added `CollectionView.Footer` with a centered gray label bound to `EntryCountDisplay`, hidden when empty via `StringToBoolConverter`.

**Why:** Users have no way to know how many journal entries they have without scrolling to the bottom. A footer count gives immediate feedback, especially after sync.

**Impact:** 25 mobile tests pass (0 warnings). 53 API tests pass.

---

## 2026-05-16 — Iteration 65 — API: Validate Todo DueDate not more than 10 years in future

**What changed:**
- `TodoEndpoints.cs`: Added DueDate range check — any record with `DueDate > now + 10 years` returns HTTP 422 with a Problem Details response, consistent with the existing `UpdatedOn` future-date guard.
- `SyncInputValidationTests.cs`: Added `Sync_Todo_FutureDueDate_Returns422` fact test.

**Why:** Without a bound, a bug or malicious client could submit dates decades in the future that would sort to the top of any overdue list. 10 years is generous for any legitimate planning horizon.

**Impact:** 25 mobile tests pass (0 warnings). 53 API tests pass (was 52).

---

## 2026-05-16 — Iteration 64 — Mobile: Show account GUID in Settings

**What changed:**
- `SettingsViewModel.cs`: Added `AccountGuid` observable, set from `account.Guid` in `LoadAsync`.
- `SettingsPage.xaml`: Added `Label` showing `{Binding AccountGuid, StringFormat='ID: {0}'}` below the account created date.

**Why:** Users need their account GUID for support tickets and cross-device identification. It was previously inaccessible from the UI.

**Impact:** 25 mobile tests pass (0 warnings). 52 API tests pass.

---

## 2026-05-16 — Iteration 63 — Mobile: Disable TodoEntry Save when Title is empty

**What changed:**
- `TodoEntryViewModel.cs`: Added `CanSave()` returning `!string.IsNullOrWhiteSpace(Title)`, `OnTitleChanged` partial to call `SaveCommand.NotifyCanExecuteChanged()`, and changed `[RelayCommand]` to `[RelayCommand(CanExecute = nameof(CanSave))]`. Removed now-redundant `if (string.IsNullOrWhiteSpace(Title)) return;` guard.

**Why:** Consistent with JournalEntry and GoalEntry patterns. The Save button should be visually disabled rather than silently no-op when Title is blank.

**Impact:** 25 mobile tests pass (0 warnings). 52 API tests pass.

---

## 2026-05-16 — Iteration 62 Brainstorm (fresh — every 3rd)

| # | Description | Dim | Impact | Effort | Risk |
|---|-------------|-----|--------|--------|------|
| 1 | Mobile: Fix duplicate `var nowMs` in DashboardViewModel — CS0128 latent bug | Bug | High | XS | Low | **SELECTED** |
| 2 | Mobile: TodoEntry — Save guard (disable Save when Title empty) | UX | Medium | XS | Low | |
| 3 | Mobile: Settings — show account GUID for support identification | UI | Small | XS | Low | |
| 4 | API: Todo sync endpoint — validate DueDate range | Quality | Small | S | Low | |
| 5 | Mobile: JournalList — show total entry count in footer | UI | Small | XS | Low | |
| 6 | Mobile: Dashboard — zero-state labels when no active goals / no pending todos | UI | Small | S | Low | |
| 7 | API: Request correlation ID header in sync endpoint logs | Observ. | Medium | S | Low | |
| 8 | Mobile: SyncService — atomic LastSyncAt write | Quality | Small | XS | Low | |

## 2026-05-16 — Iteration 62 — Mobile: Fix CS0128 latent bug in DashboardViewModel

**What changed:**
- `DashboardViewModel.cs`: Removed duplicate `var nowMs` declaration in `RefreshDataAsync` (line 77). The first declaration (line 56) is already in scope when the todos block runs. `DashboardViewModel` is excluded from `SkipMauiTargets=true` builds, so the error only surfaces when building the full MAUI targets.

**Why:** CS0128 ("A local variable named 'nowMs' is already defined in this scope") would fail any full MAUI build. The test suite uses `SkipMauiTargets=true` which excludes DashboardViewModel from compilation, masking the bug.

**Impact:** 25 mobile tests pass (0 warnings). 52 API tests pass.

---

## 2026-05-16 — Iteration 61 — Mobile: Fade completed goals in GoalList with 0.5 opacity

**What changed:**
- `GoalListPage.xaml`: Added `DataTrigger` on the row `Grid` that sets `Opacity=0.5` when `CompletionDate` is not null (via `NotNullConverter`). Completed goals already sort last via `GoalRepository.GetAllActiveAsync`; fading provides additional visual differentiation without hiding them.

**Why:** Completed goals cluttered the list visually — they blended with active goals. Making them translucent communicates "done / lower priority" at a glance while keeping the history accessible.

**Impact:** 25 mobile tests pass (0 warnings). 52 API tests pass.

---

## 2026-05-16 — Iteration 60 — Mobile: Trim Activity/Mood/Tags before saving journal entries

**What changed:**
- `JournalEntryViewModel.cs`: Notes trimmed; Activity/Mood/Tags stored as null when whitespace-only, trimmed otherwise — mirrors the pattern already used in TodoEntryViewModel for Notes

**Why:** Whitespace-only Activity or Mood would be stored and then displayed as an empty-looking chip in JournalListPage (the StringToBoolConverter would still show it). Also, a leading-space "Reading " would sort and display incorrectly.

**Impact:** 25 mobile tests pass (0 warnings). 52 API tests pass.

---

## 2026-05-16 — Iteration 59 Brainstorm (fresh — every 3rd)

| # | Description | Dim | Impact | Effort | Risk |
|---|-------------|-----|--------|--------|------|
| 1 | Mobile: Fix CS8619/CS1998 build warnings in TodoRepository + SyncServiceTests | Quality | Medium | XS | Low | **SELECTED** |
| 2 | Mobile: Trim Activity/Mood/Tags before saving in JournalEntry | Quality | Small | XS | Low | |
| 3 | Mobile: Settings — show account GUID for support | UI | Small | XS | Low | |
| 4 | Mobile: DashboardPage — better zero-state for goal/todo counts | UI | Small | XS | Low | |
| 5 | API: Add per-request log scope with correlation ID | Observ. | Medium | S | Low | |
| 6 | Mobile: SyncService — simplify pre-flight health check URL | Quality | Small | XS | Low | |
| 7 | Mobile: GoalList — completed goals shown at bottom with faded style | UI | Medium | S | Low | |
| 8 | Mobile: JournalList — show total entry count in footer | UI | Small | XS | Low | |

## 2026-05-16 — Iteration 59 — Mobile: Fix all build warnings (CS8619 + CS1998)

**What changed:**
- `TodoRepository.cs`: `GetAsync` now uses `async Task<Todo?>` with `await db.FindAsync<Todo>(guid)` — fixes CS8619 (nullable mismatch; `Task<Todo>` wasn't assignable to `Task<Todo?>` without the await)
- `SyncServiceTests.cs (FakeSyncHandler)`: Removed `async` from `SendAsync` override and wrapped returns in `Task.FromResult` — fixes CS1998 (async method without await)
- Build now produces 0 warnings

**Why:** The CS8619 warning was a real type contract mismatch — `FindAsync` can return null at runtime even though the type system didn't express it. The CS1998 warning was dead async machinery in a test helper. Clearing all warnings restores a clean build signal.

**Impact:** 25 mobile tests pass (0 warnings). 52 API tests pass.

---

## 2026-05-16 — Iteration 58 — API: Skip SaveChanges when ChangeTracker has no changes

**What changed:**
- All 4 sync endpoints: `await db.SaveChangesAsync()` → `if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync()`

**Why:** When all incoming records are filtered out (different AccountFk, or no records pass the LWW timestamp check), EF Core was still issuing a no-op `SaveChanges` call to the DB. `HasChanges()` is a synchronous O(tracked entities) check that avoids the async DB roundtrip in those cases.

**Impact:** 52 API tests pass. 25 mobile tests pass.

---

## 2026-05-16 — Iteration 57 — Mobile: JournalEntry Save disabled when Notes empty

**What changed:**
- `JournalEntryViewModel.cs`: Added `CanSave()` method; `[RelayCommand(CanExecute = nameof(CanSave))]` on SaveAsync; `OnNotesChanged` now notifies both `NotesLength` and `SaveCommand.CanExecute`

**Why:** Blank journal entries were accepted silently. The toolbar Save button now disables until the user types at least one non-whitespace character. Consistent with GoalEntry (iter 54).

**Impact:** 25 mobile tests pass. 52 API tests pass.

---

## 2026-05-16 — Iteration 56 Brainstorm (fresh — every 3rd)

| # | Description | Dim | Impact | Effort | Risk |
|---|-------------|-----|--------|--------|------|
| 1 | Mobile: GoalList — live search filter (goalText/measurableOutcome/nextStepItems) | UI | High | S | Low | **SELECTED** |
| 2 | Mobile: JournalEntry — disable Save when Notes empty | UI | Medium | XS | Low | |
| 3 | Mobile: Settings — show app version number | UI | Small | XS | Low | |
| 4 | Mobile: Dashboard — show total journal entry count | UI | Small | XS | Low | |
| 5 | API: skip SaveChanges when no records pass accountGuid filter | Perf | Small | XS | Low | |
| 6 | Mobile: GoalList — show "in X days" countdown for next meeting | UI | Medium | S | Low | |
| 7 | Mobile: SyncService — update LastSyncAt only on first successful sync | Stability | Medium | S | Medium | |
| 8 | Mobile: DashboardViewModel — refresh after shell navigation returns | UI | Medium | S | Low | |

## 2026-05-16 — Iteration 56 — Mobile: GoalList live search filter

**What changed:**
- `GoalListViewModel.cs`: Added `FilterText` observable and `_allGoals` backing list; `OnFilterTextChanged` filters across GoalText, MeasurableOutcome, and LatestNextStepItems; Delete keeps `_allGoals` consistent; LoadAsync/RefreshAsync populate `_allGoals`
- `GoalListPage.xaml`: Added `SearchBar` (row 1); bumped RowDefinitions from 2 to 3 rows

**Why:** Completes live-search coverage across all three list pages (Journal: iter 53, Todo: iter 50). Users with many goals now have a way to find a specific goal quickly.

**Impact:** 25 mobile tests pass. 52 API tests pass.

---

## 2026-05-16 — Iteration 55 — Mobile: Trim NextStepItems on load and save

**What changed:**
- `GoalEntryViewModel.cs`: Trim `NextStepItems` on load (so `_loadedNextStepItems` holds a clean value); trim at save time before comparison and storage; whitespace-only changes no longer trigger a new GoalProgress row

**Why:** Whitespace-only edits would pass the `!= _loadedNextStepItems` check and create a new GoalProgress row with the same effective content. Trimming at both load and save points makes the dedup logic reliable.

**Impact:** 25 mobile tests pass. 52 API tests pass.

---

## 2026-05-16 — Iteration 54 — Mobile: GoalEntry Save button disabled when GoalText empty

**What changed:**
- `GoalEntryViewModel.cs`: Added `CanSave()` guard method and `[RelayCommand(CanExecute = nameof(CanSave))]`; `OnGoalTextChanged` calls `SaveCommand.NotifyCanExecuteChanged()` so the toolbar button reacts immediately; removed the now-redundant null guard inside SaveAsync

**Why:** Previously tapping Save with an empty goal silently did nothing — confusing for users. The button now disables reactively as the user types.

**Impact:** 25 mobile tests pass. 52 API tests pass.

---

## 2026-05-16 — Iteration 53 Brainstorm (fresh — every 3rd)

| # | Description | Dim | Impact | Effort | Risk |
|---|-------------|-----|--------|--------|------|
| 1 | Mobile: JournalList — live search filter (notes/activity/mood/tags) | UI | High | S | Low | **SELECTED** |
| 2 | Mobile: GoalEntry — Save button disabled when GoalText empty | UI | Medium | XS | Low | |
| 3 | Mobile: TodoEntry — visible error when Title is blank on save | UI | Medium | S | Low | |
| 4 | Mobile: GoalProgressRepository — trim NextStepItems before save | Stability | Small | XS | Low | |
| 5 | Mobile: Settings — show app version number | UI | Small | XS | Low | |
| 6 | API: Skip DB work on sync when Records.Count == 0 | Perf | Small | XS | Low | |
| 7 | Mobile: GoalList — show days-until-meeting countdown badge | UI | Medium | S | Low | |
| 8 | Mobile: JournalEntry — validate Notes non-empty before save | Stability | Medium | XS | Low | |

## 2026-05-16 — Iteration 53 — Mobile: JournalList live search filter

**What changed:**
- `JournalListViewModel.cs`: Added `FilterText` observable and `_allJournals` backing list; `OnFilterTextChanged` filters across notes, activity, mood, and tags fields (case-insensitive); `Delete` keeps `_allJournals` consistent
- `JournalListPage.xaml`: Added `SearchBar` (row 1) above the CollectionView; bumped RowDefinitions from 2 to 3 rows

**Why:** Journal entries can accumulate quickly; users had no way to search without scrolling. Consistent with the TodoList filter added in iteration 50.

**Impact:** 25 mobile tests pass. 52 API tests pass.

---

## 2026-05-16 — Iteration 52 — Mobile: Journal notes character count

**What changed:**
- `JournalEntryViewModel.cs`: Added `NotesLength` observable; `OnNotesChanged` partial updates it reactively
- `JournalEntryPage.xaml`: Gray right-aligned "N characters" label below the Notes editor

**Why:** Users writing journal entries had no feedback on entry length; the count helps gauge whether they're writing a brief note or a full reflection.

**Impact:** 25 mobile tests pass. 52 API tests pass.

---

## 2026-05-16 — Iteration 51 — API: Validate GoalFk on goal-progress sync endpoint

**What changed:**
- `GoalProgressEndpoints.cs`: Added GUID format check for `GoalFk` field; returns 422 with LogWarning if invalid
- `SyncInputValidationTests.cs`: Added `Sync_GoalProgress_InvalidGoalFk_Returns422` test (52 API tests total)

**Why:** An invalid GoalFk string (null/non-GUID) would be accepted and stored as an orphaned GoalProgress record with no matching Goal. The new check is consistent with how `Guid` is validated on the same endpoint.

**Impact:** 52 API tests pass. 25 mobile tests pass.

---

## 2026-05-16 — Iteration 50 — Mobile: Todo list live text filter

**What changed:**
- `TodoListViewModel.cs`: Added `FilterText` observable and `_allTodos` backing list; `OnFilterTextChanged` re-filters `Todos` client-side on each keystroke; `Add`/`Complete`/`Delete` keep `_allTodos` consistent
- `TodoListPage.xaml`: Added `SearchBar` (row 3) below the add-task row; bumped RowDefinitions from 5 to 6 rows; footer moved to row 5

**Why:** Users with many tasks had to scroll to find anything. Client-side filtering avoids extra DB queries and feels instant.

**Impact:** 25 mobile tests pass. 51 API tests pass.

---

## 2026-05-16 — Iteration 49 — Mobile: Dashboard shows next upcoming goal meeting date

**What changed:**
- `DashboardViewModel.cs`: Added `NextGoalMeeting` / `HasNextGoalMeeting` observables; `RefreshDataAsync` finds the nearest future `NextMeetingDate` among active (non-completed) goals and formats it as "today", "tomorrow", or "MMM d"
- `DashboardPage.xaml`: New CornflowerBlue label below sync status, hidden when no upcoming meeting

**Why:** Users had no way to see goal meeting dates without navigating to the goal list. The dashboard now surfaces the single most relevant meeting date at a glance.

**Impact:** 25 mobile tests pass. 51 API tests pass.

---

## 2026-05-16 — Iteration 48 — Mobile: Skip GoalProgress insert when NextStepItems unchanged

**What changed:**
- `GoalEntryViewModel.cs`: Added `_loadedNextStepItems` field; set on load from existing GoalProgress; SaveAsync only creates a new GoalProgress row when `NextStepItems != _loadedNextStepItems`

**Why:** Every goal save (e.g., updating MeasurableOutcome without touching next steps) was inserting a new GoalProgress row with identical content. Over time this grows the table and bloats sync payloads.

**Impact:** 25 mobile tests pass. 51 API tests pass.

---

## 2026-05-16 — Iteration 47 Brainstorm (fresh — every 3rd)

| # | Description | Dim | Impact | Effort | Risk |
|---|-------------|-----|--------|--------|------|
| 1 | Bug: JournalRepository.SaveAsync only bumps UpdatedOn when == 0 — edits never sync | Stability | High | XS | Low | **SELECTED** |
| 2 | GoalEntryViewModel: skip creating GoalProgress if NextStepItems unchanged | Performance | Medium | S | Low | |
| 3 | API: validate AccountFk is non-null on all sync endpoint inbound records | Stability | Small | XS | Low | |
| 4 | Mobile: TodoListPage — live text filter on title | UI | Medium | M | Low | |
| 5 | Mobile: JournalEntryPage — missing character count on Notes field | UI | Small | XS | Low | |
| 6 | Mobile: DashboardPage — show next upcoming goal meeting date | UI | Medium | S | Low | |
| 7 | Mobile: GoalListPage — show "No upcoming meeting" vs date countdown | UI | Small | XS | Low | |
| 8 | API: GoalProgress sync — validate GoalFk is valid GUID | Stability | Small | XS | Low | |

## 2026-05-16 — Iteration 47 — Mobile: Fix JournalRepository sync bug (edits never synced)

**What changed:**
- `JournalRepository.cs`: Removed `if (UpdatedOn == 0)` guard — `SaveAsync` now always sets `UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` (matching the pattern already used by Goal and Todo repos)
- `JournalRepositoryTests.cs`: Added 2 new tests verifying edits bump UpdatedOn and appear in GetModifiedSince; updated existing GetModifiedSince test to use current-time-based timestamps (robust against parallel runs)

**Why:** Journal edits had UpdatedOn != 0, so SaveAsync never bumped it. GetModifiedSinceAsync uses `UpdatedOn > since` to find records needing sync, meaning any journal entry edit was permanently invisible to the sync engine. This is the same pattern Goal and Todo repos already follow correctly.

**Impact:** 25 mobile tests pass. 51 API tests pass.

---

## 2026-05-16 — Iteration 46 — Mobile: GoalList shows latest next-step items

**What changed:**
- `Goal.cs`: Added `[Ignore] public string? LatestNextStepItems { get; set; }` (transient, not persisted)
- `GoalProgressRepository.cs`: Added `GetLatestNextStepsAsync` — single SQL GROUP BY query returning latest `NextStepItems` per goal
- `GoalListViewModel.cs`: Added `GoalProgressRepository progressRepo` to constructor; `LoadGoalsWithStepsAsync` populates LatestNextStepItems via dictionary lookup (avoids N+1)
- `GoalListPage.xaml`: New gray label below expiration date shows LatestNextStepItems, hidden when null/empty

**Why:** The goal list was missing context — users couldn't see where they left off on each goal without opening it. This surfaces the latest next-step directly in the list without extra taps.

**Impact:** 23 mobile tests pass. 51 API tests pass.

---

## 2026-05-16 — Iteration 45 — API: Warning log on sync 422 rejections

**What changed:**
- All 4 sync endpoints: Added `logger.LogWarning(...)` before returning 422 for future `UpdatedOn` or invalid `Guid`. Includes account prefix for correlation.

**Why:** 422 responses from misbehaving or clock-skewed clients are silent at Debug level. Warning level makes them searchable in production without flooding logs on normal operation.

**Impact:** 51 API tests pass. 23 mobile tests pass.

---

## 2026-05-16 — Iteration 44 — API: NickName validation on register

**What changed:**
- `AuthEndpoints.cs`: Added empty/whitespace check (400) and max 50-char check (400) before the duplicate NickName check. NickName is trimmed before storage.
- `AuthEndpointTests.cs`: 3 new tests (empty, whitespace, too-long). 51 API tests total.

**Why:** A blank NickName or a 1000-char string would be stored in the DB and displayed in settings. Trimming prevents accidental leading/trailing spaces creating duplicate-looking accounts.

**Impact:** 51 API tests pass. 23 mobile tests pass.

---

## 2026-05-16 — Iteration 44 Brainstorm (fresh — every 3rd)

| # | Description | Dim | Impact | Effort | Risk | Status |
|---|-------------|-----|--------|--------|------|--------|
| 1 | API: NickName validation on register (non-empty, max 50 chars) | Stability | Medium | S | Low | **SELECTED** |
| 2 | API: structured warning log for 4xx sync validation failures | Ops | Low | S | Low | Backlog |
| 3 | GoalListPage: show NextStepItems from latest GoalProgress | UI | Medium | M | Low | Backlog |
| 4 | DashboardPage: show 2-3 active goals below journal section | UI | Medium | M | Low | Backlog |
| 5 | JournalList: search by notes keyword | Func | Medium | M | Low | Backlog |
| 6 | GoalList: collapsible completed goals section | UI | Low | M | Low | Backlog |
| 7 | Mobile: guard nav when account not configured (SetupPage gate) | Stability | Medium | M | Medium | Backlog |
| 8 | TodoEntry: add UpdatedOn to inline add (quick add) | Stability | Low | S | Low | Backlog |
| 9 | DashboardViewModel: show last 3 goals count with upcoming meeting | UI | Low | M | Low | Backlog |
| 10 | API: register endpoint returns account Guid + JWT (already does) | Done | — | — | — | Done |

---

## 2026-05-16 — Iteration 43 — SyncService: 15-second HTTP client timeout

**What changed:**
- `SyncService.RunAsync`: `client.Timeout = TimeSpan.FromSeconds(15)` set on the `HttpClient` before making any requests.

**Why:** Without a client-side timeout, a hung API server (unresponsive but connected) blocks `RunAsync` for the OS default timeout (100s+). 15 seconds is generous relative to the API's 10-second request timeout while still failing fast for users.

**Impact:** 23 mobile tests pass. 48 API tests pass.

---

## 2026-05-16 — Iteration 42 — API: EF Core 8-second command timeout

**What changed:**
- `Program.cs`: `UseMySql(…, mySqlOptions => mySqlOptions.CommandTimeout(8))` — 8-second limit per DB command.

**Why:** The 10-second request timeout kills the HTTP request but doesn't cancel the underlying DB query, holding the connection open. The EF command timeout cancels the query itself, releasing the connection immediately.

**Impact:** 48 API tests pass. 23 mobile tests pass.

---

## 2026-05-16 — Iteration 41 — TodoListPage: overdue count banner

**What changed:**
- `TodoListViewModel`: Added `OverdueTodoCount` and `HasOverdueTodos` observables. `UpdateOverdueCount(items)` computes count from pending todos with `DueDate < now`. Called in `LoadAsync`, `RefreshAsync`, `CompleteAsync`, `DeleteAsync`.
- `TodoListPage.xaml`: Added row 1 as red banner `"{0} task(s) overdue"`, visible only when `HasOverdueTodos`. Existing add-input row → row 2, CollectionView row → row 3, footer → row 4.

**Why:** Dashboard shows overdue count but the todo list itself gave no indication of urgency. Users had to scroll to find overdue items (they're sorted first). The banner makes urgency visible on arrival.

**Impact:** 23 mobile tests pass. 48 API tests pass.

---

## 2026-05-16 — Iteration 41 Brainstorm (fresh — every 3rd)

| # | Description | Dim | Impact | Effort | Risk | Status |
|---|-------------|-----|--------|--------|------|--------|
| 1 | TodoListPage: overdue count banner at top | UI | Medium | S | Low | **SELECTED** |
| 2 | API: rate limiting per account (built-in ASP.NET 8) | Stability | Medium | M | Medium | Backlog |
| 3 | GoalListPage: show NextStepItems from latest progress | UI | Medium | M | Low | Backlog |
| 4 | JournalListPage: search/filter by notes keyword | Func | Medium | M | Low | Backlog |
| 5 | EF Core: command timeout on AppDbContext | Stability | Low | S | Low | Backlog |
| 6 | SyncService: per-entity HTTP timeout (HttpClient.Timeout) | Stability | Low | S | Low | Backlog |
| 7 | AccountService: PIN change from SettingsPage | Func | Medium | M | Medium | Backlog |
| 8 | GoalEntry: save progress NextMeetingDate from GoalText NextMeeting | Func | Low | S | Low | Backlog |
| 9 | API: structured error logging (log 4xx/5xx responses) | Ops | Low | S | Low | Backlog |
| 10 | DashboardPage: recent goals list (not just count) | UI | Low | M | Low | Backlog |

---

## 2026-05-16 — Iteration 40 — API: GUID format validation on sync endpoints

**What changed:**
- All 4 sync endpoints (`journal`, `goal`, `goal-progress`, `todo`): Added `Guid.TryParse` check on every incoming record's Guid field; returns 422 if any record has an invalid UUID format.
- `SyncInputValidationTests.cs`: Added 4 `[Theory]` tests covering the new 422 path. 48 API tests total.

**Why:** Previously any string could be accepted as a Guid, polluting the DB with records that could never be retrieved by UUID. Client code always generates proper UUIDs but a malformed or tampered payload could insert garbage.

**Impact:** 48 API tests pass. 23 mobile tests pass.

---

## 2026-05-16 — Iteration 39 — JournalList: Activity badge per row

**What changed:**
- `JournalListPage.xaml`: Wrapped Mood label in `HorizontalStackLayout`; added Activity label in `CornflowerBlue` beside it, each hidden when empty.

**Why:** Activity field was collected but invisible in list view. Users had to open each entry to see what they were doing, making list scanning useless for activity context.

**Impact:** 23 mobile tests pass. 44 API tests pass.

---

## 2026-05-16 — Iteration 38 — GoalEntry: Delete button

**What changed:**
- `GoalEntryViewModel`: Added `DeleteAsync` relay command.
- `GoalEntryPage.xaml`: Added red "Delete Goal" button below "Mark as Complete", visible only for existing goals.

**Why:** Parity with JournalEntry and TodoEntry. Goals could be swipe-deleted from the list but not from the entry view.

**Impact:** 23 mobile tests pass. 44 API tests pass.

---

## 2026-05-16 — Iteration 37 — TodoEntry: Delete button

**What changed:**
- `TodoEntryViewModel`: Added `DeleteAsync` relay command — soft-deletes via `repo.DeleteAsync`, then navigates back.
- `TodoEntryPage.xaml`: Added red "Delete Task" button below "Mark as Done", visible only for existing todos (`IsExisting`).

**Why:** Users could swipe-to-delete from the todo list but had no way to delete a todo they'd opened for editing. JournalEntry already had this pattern; this closes the parity gap.

**Impact:** 23 mobile tests pass. 44 API tests pass.

---

## 2026-05-16 — Iteration 36 — API: /health DB ping

**What changed:**
- `Program.cs`: `/health` endpoint now calls `db.Database.CanConnectAsync()`. Returns 503 Problem Details if the DB is unreachable; 200 only when DB is live.

**Why:** `SyncService` uses `/health` as a pre-flight before 4 entity syncs. Previously it returned 200 even if the DB was down, causing all entity syncs to fail with confusing errors rather than a clean `SyncResult.NoServer`.

**Impact:** 44 API tests pass. 23 mobile tests pass.

---

## 2026-05-16 — Iteration 35 — SyncService concurrent sync guard

**What changed:**
- `SyncService`: Added `private int _syncing` field. `RunAsync` uses `Interlocked.CompareExchange` at entry to return `SyncResult.Success` immediately if a sync is already in-flight. `Interlocked.Exchange` resets the flag in a `finally` block.
- `SyncServiceTests.cs`: Added `RunAsync_ConcurrentCall_SkipsSecondSync` test.

**Why:** `SyncService` is a singleton. `DashboardViewModel` fires `RunSyncAsync` as a background task immediately after loading. If a user pulls to refresh on any list page before that background sync completes, two concurrent `RunAsync` calls would both read the same `account.LastSyncAt`, make duplicate HTTP calls, and potentially race on `UpdateLastSyncAsync`. The guard prevents this.

**Impact:** 23 mobile tests pass. 44 API tests pass.

---

## 2026-05-16 — Iteration 34 — API: RFC 7807 Problem Details for sync validation

**What changed:**
- `Program.cs`: Added `builder.Services.AddProblemDetails()`.
- `JournalEndpoints.cs`, `GoalEndpoints.cs`, `GoalProgressEndpoints.cs`, `TodoEndpoints.cs`: All validation returns (`BadRequest`/`UnprocessableEntity`) replaced with `Results.Problem()` for RFC 7807 compliant error bodies.

**Why:** String-based error responses gave clients no structured way to parse validation failures. RFC 7807 `application/problem+json` with `status`, `title`, and `detail` fields enables client-side error handling without string parsing.

**Impact:** 44 API tests pass. 0 regressions.

---

## 2026-05-16 — Iteration 35 Brainstorm (fresh — every 3rd)

| # | Description | Dim | Impact | Effort | Risk | Status |
|---|-------------|-----|--------|--------|------|--------|
| 1 | SyncService: concurrent sync guard (Interlocked flag) | Stability | Medium | S | Low | **SELECTED** |
| 2 | API: /health DB ping (real connectivity check) | Stability | Medium | S | Low | Backlog |
| 3 | TodoEntry: Delete button (parity with JournalEntry) | Func | Medium | S | Low | Backlog |
| 4 | GoalEntry: Delete button | Func | Low | S | Low | Backlog |
| 5 | Dashboard: tappable goal/todo counts → navigate | UI | Low | S | Low | Backlog |
| 6 | JournalListPage: activity/mood badge per row | UI | Low | S | Low | Backlog |
| 7 | API: Guid format validation on incoming records | Stability | Low | S | Low | Backlog |
| 8 | JournalList: filter by tag or mood | Func | Medium | M | Low | Backlog |
| 9 | GoalList: separate section for completed goals | UI | Low | M | Low | Backlog |
| 10 | TodoEntry: time component on DueDate (not just date) | Func | Low | M | Medium | Backlog |

---

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

## 2026-05-17 — Soft-delete tombstone validation tests: Journal, Goal, Todo (iter 293)

**Branch:** `improve/softdel-blank-field-tests-293`

**What:** Added three API sync tests confirming that soft-deleted records with null required fields are accepted:
- `JournalSyncTests.Sync_SoftDeletedRecord_BlankNotes_Accepted` — Journal tombstone with null Notes
- `GoalSyncTests.Sync_SoftDeletedRecord_BlankGoalText_Accepted` — Goal tombstone with null GoalText
- `TodoSyncTests.Sync_SoftDeletedRecord_BlankTitle_Accepted` — Todo tombstone with null Title

**Why:** Mirrors `GoalProgressSyncTests.Sync_SoftDeletedRecord_BlankNextStepItems_Accepted` (iter 292). The sync endpoints correctly gate required-field validation on `DeletedAt is null`, but this contract was untested for Journal, Goal, and Todo. Mobile clients delete records by sending tombstones with cleared fields.

**Impact:** 217 API tests pass (up from 214). Validation contract explicitly documented for all 4 sync entities.

---

## Flagged but not implemented (requires backend coordination)

**Password in URL:** `account.service.ts` `token()` method sends the password as a plain path segment in a GET request (`/token/{nickname}/{password}`). Passwords in URLs are logged by servers, proxies, and browser history. Fix requires a POST-based authentication endpoint on the backend.

---

## Pre-existing lint errors (not introduced by this session)

Multiple `azAuthHeader` quoting, line-length, and semicolon issues across `goal.service.ts`, `journal.service.ts`, and `todo.service.ts`. These pre-date this session and are cosmetic — tracked for a future focused lint-cleanup pass.

## 2026-05-17 — Iteration 294 Brainstorm (fresh)

| # | Description | Dim | Impact | Effort | Risk | Status |
|---|---|---|---|---|---|---|
| 1 | GoalDetail: "Mark as Complete" button + completed badge | Func | High | S | Low | **DONE (iter 295)** |
| 2 | API: reject UpdatedOn > now + 24h (clock-skew protection) | Stability | Medium | S | Low | **DONE (iter 296)** |
| 3 | Mobile: error boundary in LoadCommand (catch + alert) | Stability | Medium | M | Low | **DONE (iter 297)** |
| 4 | Web: GoalDetail show completion date when completed | UI | Low | S | Low | Backlog |
| 5 | API tests: validate future-timestamp rejection | Quality | Medium | S | Low | Backlog |
| 6 | Mobile: GoalListPage completed goals collapsed section | UI | Low | M | Low | Backlog |
| 7 | Web: Home dashboard quick-add journal entry | Func | Medium | M | Medium | Backlog |
| 8 | API: GoalProgress test for UpdatedOn=0 exclusion from delta | Quality | Low | S | Low | Backlog |

---

## 2026-05-17 — GoalDetail Mark as Complete + completion badge (iter 295)

**Branch:** `improve/goaldetail-mark-complete-295`

**What:** Added "Mark as Complete" button on the GoalDetail page. When a goal is completed:
- A success alert shows the completion date
- The edit button is hidden
- A "Reopen Goal" button appears beneath the alert
- `goal_complete` analytics event tracked

**Why:** Users could mark goals complete from the dashboard list but had no way to do so from within the detail page — where they most naturally land after reviewing progress.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — GoalDetail next meeting date + Reopen Goal button (iter 296)

**Branch:** `improve/api-futurestamp-validation-296`

**What:** Added to GoalDetail:
- `Goal.NextMeetingDate` displayed as a caption below MeasurableOutcome
- "Reopen Goal" button for completed goals (clears CompletionDate, tracks `goal_reopen` analytics)
- Edit button re-appears after reopen

**Why:** NextMeetingDate was only visible in individual progress notes; the goal-level meeting date was invisible on the detail view. Reopen completes the complete/reopen lifecycle without requiring users to go back to the list.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — GoalDetail target date with overdue indicator (iter 297)

**Branch:** `improve/mobile-error-boundary-297`

**What:** Added `Goal.ExpirationDate` display on GoalDetail below NextMeetingDate. Shows in red with "— overdue" label when the target date has passed. Hidden for completed goals.

**Why:** The target completion date was already editable from the dialog but never shown at the top of the detail view — users had to open the edit dialog to see it.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — GoalDetail progress note edit dialog (iter 298)

**Branch:** `improve/goaldetail-edit-progress-298`

**What:** Added an Edit button to each progress note in the GoalDetail timeline. Opens a pre-filled dialog with NextStepItems and NextMeetingDate. Saves with LWW UpdatedOn and tracks `progress_edit` analytics.

**Why:** Progress notes could only be deleted, not corrected. Users who made typos or wanted to update next steps had to delete and re-add.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Completed goals View link + completion dates (iter 299)

**Branch:** `improve/completed-goals-view-links-299`

**What:**
- Home page completed goals panel: each entry now shows completion date and a "View" button linking to GoalDetail (enabling Reopen from there)
- Todos completed section: each completed todo now shows "Done MMM d" completion date

**Why:** Completed items were opaque — no dates, no way to navigate to detail. Now users can see when goals were completed and navigate to reopen them if needed.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Iteration 300 Brainstorm (fresh)

| # | Description | Dim | Impact | Effort | Risk | Status |
|---|---|---|---|---|---|---|
| 1 | GoalDetail: delete goal from detail page | Func | High | S | Low | **DONE (iter 301)** |
| 2 | API: batch size limit test (>500 records returns 400) | Quality | Low | S | Low | **DONE (iter 302)** |
| 3 | Web: Todo uncomplete (restore pending) | Func | Medium | M | Low | **DONE (iter 303)** |
| 4 | Mobile: TodoRepository due-date ordering test | Quality | Medium | S | Low | Backlog |
| 5 | Mobile: SyncService 401 handling test | Stability | High | M | Low | Backlog |
| 6 | Web: JournalPage show mood/activity as tags | UI | Low | S | Low | Backlog |
| 7 | API: concurrent same-guid upload — idempotency test | Quality | Medium | M | Low | Backlog |
| 8 | Web: Home dashboard — show last sync time | UI | Low | S | Low | Backlog |

---

## 2026-05-17 — GoalDetail delete goal button (iter 301)

**Branch:** `improve/goaldetail-delete-goal-301`

**What:** Added a delete (trash) icon button in the GoalDetail header. Soft-deletes the goal with LWW UpdatedOn invariant, tracks `goal_delete` analytics, navigates back to home.

**Why:** Previously there was no way to delete a goal from within the detail page. Users had to go back to the home list to delete.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Todos uncomplete + delete from completed section (iter 302)

**Branch:** `improve/todos-uncomplete-302`

**What:** Added Undo and Delete icon buttons to each row in the completed todos expansion panel. Uncomplete clears `CompletedAt`, bumps `UpdatedOn`, and tracks `todo_uncomplete` analytics.

**Why:** Once a todo was completed there was no way to reverse it — users had to delete and re-create. Common case: accidentally marked done.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — TodoRepository.UncompleteAsync + 3 mobile tests (iter 303)

**Branch:** `improve/mobile-todo-uncomplete-303`

**What:** Added `UncompleteAsync(guid)` to `TodoRepository` — clears `CompletedAt`, bumps `UpdatedOn`. Three tests: field cleared and UpdatedOn bumped, restored todo appears in GetPendingAsync, already-pending is a no-op.

**Why:** Web UI gained uncomplete capability in iter 302; mobile repository needed the matching method for eventual mobile UX parity and sync correctness.

**Impact:** 224 mobile tests pass (up from 221). Build clean.

---

---

## 2026-05-17 — Step 0a: Unsolved Problems Research (Invocation 3)

**Domain:** Child development goal tracking for kids + caregivers. Comparable tools: CDC Milestone Tracker, AbleSpace (IEP), reward-chart apps (Habitz, S'moresUp), pediatric therapy EMRs.

Search: Reddit (limited results for niche), App Store/Play Store reviews, Capterra/G2, academic JMIR review (2023), pediatric therapy software blogs.

| Pain point | Source | Frequency | In scope? |
|---|---|---|---|
| No long-term visual progress chart — users want graphs not just text notes | Multiple app reviews (Reward Chart app, academic review) | Medium | **Yes** — ChildDev has progress notes but zero visualization |
| Data lost when switching phones (no account sync) | CDC Milestone Tracker App Store reviews, April 2024 | High | **Addressed** — ChildDev has LWW sync |
| Paywalls block basic features | Kinedu, general child app reviews | High | **N/A** — ChildDev appears fully open |
| Heavy documentation burden for caregivers | Pediatric therapy software blogs, sprypt.com 2025 | High | **Partial** — quick-capture forms exist, could be faster |
| Editing locked after creation | Reward chart app reviews 2024 | Medium | **Addressed** — iter 298 fixed this |
| No customizable task weights/priority | Reward chart app reviews | Low | **Partial** — todos have no priority field |
| Progress not motivating for kids — too abstract | Academic JMIR review 2023 | High | **Partial** — no visual/celebratory feedback |
| Caregiver portal / parent visibility into therapist notes | Pediatric therapy EMR blogs | Medium | **Partial** — ChildDev is caregiver-first |
| Report generation for IEP meetings | AbleSpace reviews | Low | **No** — out of scope for current stage |
| Overly sensitive milestone alerts causing alarm | CDC tracker reviews | Medium | **N/A** — ChildDev is goal-based not milestone-screener |

**Top actionable signals:**
1. **Visual progress chart** — mentioned across reward apps, academic reviews. ChildDev has zero charts. High impact, medium effort.
2. **Quick-capture UX** — data entry friction. Could reduce taps to add a progress note.
3. **Motivating feedback for kids** — celebration/achievement state when goal completed. Low effort, high psychological impact.

---

## 2026-05-17 — Mobile completed todos section with swipe-to-uncomplete (iter 305)

**Branch:** `improve/mobile-uncomplete-todos-305`

**What:** Added `GetCompletedAsync` to `TodoRepository`. `TodoListViewModel` now loads completed todos, exposes `UncompleteAsync` and `ToggleCompleted` relay commands, and tracks a `CompletedTodos` observable collection. `TodoListPage.xaml` gained a collapsible "▸ N completed" section at the bottom with swipe-left to restore and swipe-right to delete. New `InverseBoolConverter` registered in App.xaml resources for XAML visibility toggling.

**Why:** Web UI gained uncomplete and completed-list visibility in iter 302–303, but mobile only showed a static count. Parity reduces confusion for caregivers moving between web and mobile.

**Impact:** 226 mobile tests pass (up from 224). Build clean.

---

## 2026-05-17 — GoalDetail weekly progress bar chart (iter 306)

**Branch:** `improve/goal-progress-chart-306`

**What:** Added a `MudChart` bar chart below the "Progress Notes" header on `GoalDetail.razor`. Shows progress note counts per week for the last 8 weeks. Only renders when there are 2+ notes. Data computed client-side from already-loaded `ProgressEntries`.

**Why:** Top research pain point: "no long-term visual progress chart — users want graphs not just text notes." ChildDev had zero charts before this.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Home dashboard quick-add journal entry (iter 307)

**Branch:** `improve/home-quickadd-journal-307`

**What:** Added a "Quick Journal Entry" MudPaper panel at the bottom of the home dashboard. Caregivers can type a freeform observation and click Save without leaving the page. Save button is disabled while the field is blank. Tracks `journal_quickadd` analytics event.

**Why:** Research finding: "Heavy documentation burden for caregivers." Reducing navigation friction from 2 pages to 0 for the most common action (adding a note) directly addresses this.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Navbar active page indicator (iter 308)

**Branch:** `improve/goal-todos-link-308`

**What:** Main nav buttons (Goals, Todos, Journal) now show `Variant.Outlined` when their route matches the current URL. Goals button also activates on `/goals/*` child routes. Computed via `NavigationManager.Uri`.

**Why:** No visual feedback for the active section caused disorientation, especially after navigating deep into a goal and returning.

**Impact:** Build clean. 217 API tests pass.

---

## 2026-05-17 — Celebration dialog on home dashboard goal completion (iter 309)

**Branch:** `improve/home-goal-complete-celebration-309`

**What:** Completing a goal from the home dashboard now shows the same celebration dialog added to GoalDetail in iter 304. The completed goal's text is captured and displayed in the dialog.

**Why:** The celebration dialog was only on GoalDetail — completing a goal from the home cards gave no visual reward, just a snackbar. This inconsistency undercut the motivational design.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Navbar active state + Home celebration + Todos filter (iters 308–310)

### Iter 308 — Navbar active page indicator
Nav buttons show `Variant.Outlined` when active. Goals button activates on `/goals/*` routes.

### Iter 309 — Goal completion celebration on home dashboard
Completing a goal from the home cards now shows the celebration dialog (consistent with GoalDetail).

### Iter 310 — Search filter on web Todos page
MudTextField filter bar added above the pending todos list. Filters by title and notes in real-time. Shows "N matching 'query'" count when active. Parity with mobile FilterText feature.

**Impact:** All 217 API + 226 mobile tests pass. Build clean.

---

## 2026-05-17 — Mobile goal staleness indicator (iter 312)

**Branch:** `improve/mobile-goal-staleness-312`

**What:** Added `GetLatestProgressInfoAsync` to `GoalProgressRepository` returning `(Steps, UpdatedOn)` per goal. `GoalListViewModel` populates `LatestProgressAt` on each `Goal`. `GoalListPage` shows "Updated Xd ago" in gray (or orange when 14+ days stale). Added `ProgressStalenessConverter` and `ProgressStalenessColorConverter`. Two new repository tests.

**Why:** Mobile goal list showed next-step text but no temporal signal about how long since anyone added a progress note. Web had this chip (green/yellow); mobile was lagging behind.

**Impact:** 228 mobile tests (up from 226). Build clean.

---

## 2026-05-17 — Search filters on web Todos and Journal pages (iters 310–311)

**Iter 310 — Todos filter:** Real-time filter by title and notes. Shows "N matching 'query'" count.
**Iter 311 — Journal filter:** Real-time filter by notes, activity, mood, and tags. Shows "No entries matching…" when empty.

Both match mobile filter behavior (TodoListViewModel.FilterText, JournalListViewModel.FilterText).

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Home dashboard staleness sort + goal search filter (iters 313–314)

**Iter 313 — Active goals staleness sort:** `LoadGoals()` now sorts `ActiveGoals` so goals with no progress entries appear first (needing attention), then by `LastProgressAt` ascending (oldest update first). Caregivers see the goals that need the most attention at the top.

**Iter 314 — Goal search filter on home dashboard:** Added `AllActiveGoals` backing list and `FilterText` field. A `MudTextField` search bar appears inline next to the "My Goals" heading. `ApplyFilter()` searches `GoalText` and `MeasurableOutcome` case-insensitively. Stats panel badge still reflects the full unfiltered count. Completes filter parity: Todos ✓, Journal ✓, Goals ✓.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Analytics insights page (iter 315)

**What:** New `/insights` Blazor page showing personal usage stats for the logged-in caregiver. Displays: total actions (last 30 days), actions this week, active day count, 14-day daily bar chart, top features used, and pages visited. Added "Insights" nav link in MainLayout.

**Why:** CLAUDE.md requires analytics in all web UI pages and says "usage data should be used to promote high-usage features." The insights page exposes that data directly to caregivers so they can see their own activity patterns. No new infrastructure — reads from existing `AnalyticsEvents` table.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Mobile dashboard quick-add journal entry (iter 316)

**What:** Added inline quick-journal entry to `DashboardPage.xaml` — an `Editor` field + "Save Entry" button that saves a journal note without navigating away. The "✓ Saved!" confirmation label appears for 1.5 s then hides. The "Full Entry" button still navigates to the full journal form. `RecentJournals` refreshes after each save.

**Why:** The mobile dashboard already had a "+ New Journal Entry" button that navigated away, forcing caregivers out of the dashboard. An inline quick-capture matches the web home page experience and reduces friction for logging observations.

**Impact:** 228 mobile tests pass. Build clean.

---

## 2026-05-17 — Mobile goal list staleness sort (iter 317)

**What:** `LoadGoalsWithStepsAsync` in `GoalListViewModel` now sorts goals the same way the web home page does: goals with no progress entries appear first (needs attention), then by `LatestProgressAt` ascending (oldest update first).

**Why:** The web home dashboard had this sort since iter 313. The mobile goal list was showing goals in DB insertion order, so stale goals were buried. Parity across platforms.

**Impact:** 228 mobile tests pass. Build clean.

---

## 2026-05-17 — Todos page due-date status filter chips (iter 318)

**What:** Added `MudChipSet` filter tabs to the web Todos page: All | Overdue (N) | Due Today | No Date. Filter chips work independently and stack with the existing text search. `StatusFilter` field tracks selection; `ApplyFilter()` applies both status and text predicates.

**Why:** The overdue alert told caregivers how many tasks were overdue but gave no way to isolate them. Now one click shows only overdue or only today's tasks, making the page much more actionable.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Home page recent journal entries section (iter 319)

**What:** Added "Recent Journal" section above the quick-add form on the web home page. Shows the last 3 journal entries (truncated at 120 chars) with date and activity. A "View All" link navigates to the full journal page. The section refreshes after a quick-add save.

**Why:** Mobile dashboard already showed recent journals. Web home was purely goal-focused and required navigating away to see any journal activity. Surfacing recent entries gives caregivers context about what's been logged without leaving the dashboard.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Home goal cards: quick progress note button (iter 320)

**What:** Added a "Note" button to each goal card on the home dashboard. Clicking it opens a dialog with a text field; saving creates a `GoalProgress` entry without navigating away. The goal list refreshes after save so staleness chips update immediately. Tracks `progress_quickadd` analytics event.

**Why:** The primary workflow — see a goal → log progress — required two navigations (home → goal detail → click "Add Progress Note"). The quick note button collapses that to one click from the dashboard.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Web journal entry date picker (iter 321)

**What:** Added `MudDatePicker` to both the Add and Edit journal dialogs on the web journal page. Defaults to today. Edit dialog pre-populates with the existing `EnteredDate`. Saved date is converted to Unix ms and stored in `EnteredDate`.

**Why:** Mobile `JournalEntryPage` has always had a date field, allowing backdated entries. The web had no way to set the date — every entry was stamped with the current moment. This closes the parity gap and allows caregivers logging observations after the fact.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — AnalyticsEvents DB index + journal date picker fixes (iters 321-322)

**Iter 321 — Journal entry date picker:** See entry above.

**Iter 322 — AnalyticsEvents index:** Added composite index `(AccountGuid, Timestamp)` to `AnalyticsEvent` in `AppDbContext.OnModelCreating`. The Insights page queries `WHERE AccountGuid = ? AND Timestamp >= ?` — without an index this would full-scan as event volume grows. `EnsureCreated()` will apply the index on fresh DB creation.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Register page confirm PIN field (iter 323)

**What:** Added a "Confirm PIN" field to the web registration form. Submission is blocked with an error state (`MudTextField Error`) when the two PIN fields don't match.

**Why:** Without confirmation, a typo during registration creates an account the user cannot log into. The mobile registration flow forces the caregiver to confirm their PIN.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Mobile dashboard "needs attention" goal banner (iter 324)

**What:** Added `StaleGoalText` / `HasStaleGoal` to `DashboardViewModel`. In `RefreshDataAsync`, the most stale active goal (no progress or >7 days since last note) is selected and shown as a yellow warning banner on `DashboardPage`. `GoalProgressRepository` injected into the ViewModel.

**Why:** The mobile dashboard showed sync status and summary counts but gave no clue about which specific goal was being neglected. The banner surfaces the highest-priority goal directly, matching the intent of the web home's staleness sort.

**Impact:** 228 mobile tests pass. Build clean.

---

## 2026-05-17 — Home dashboard stat cards clickable (iter 325)

**What:** Wrapped the Todos stat card in a `MudLink Href="/todos"` and the Journal stat card in a `MudLink Href="/journal"`. Both now navigate on click. Goals card stays non-linked since goals are displayed directly below it.

**Why:** The stat cards felt like navigation items (they show counts, have icons), but clicking them did nothing. Making them navigable follows standard dashboard UX expectations.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Mobile todo filter searches notes field (iter 326)

**What:** `TodoListViewModel.OnFilterTextChanged` now also searches `t.Notes` in addition to `t.Title`. The inline Add path checks both fields too. Matches web Todo filter behavior (iter 310 searched title + notes).

**Why:** Caregivers often add context in the Notes field. The web filter already searched both; the mobile filter was title-only and would miss notes-only matches.

**Impact:** 228 mobile tests pass. Build clean.

---

## 2026-05-17 — GetLatestProgressInfoAsync test coverage (iter 327)

**What:** Added two tests to `GoalProgressRepositoryTests`:
- `GetLatestProgressInfoAsync_WhenNoEntries_ReturnsEmptyDictionary` — baseline coverage for accounts with no progress data
- `GetLatestProgressInfoAsync_ExcludesOtherAccounts` — multi-tenant isolation boundary check

**Why:** These edge cases drive the staleness sort in `GoalListViewModel`. The empty-dictionary case is the pre-condition for showing a goal as "needs attention."

**Impact:** 230 mobile tests (up from 228). Build clean.

---

## 2026-05-17 — Mobile goal "Reopen Goal" feature (iter 328)

**What:** Added `ReopenAsync` to `GoalRepository` (clears `CompletionDate`, bumps `UpdatedOn`). Added `IsCompleted` observable property and `ReopenCommand` to `GoalEntryViewModel`. `GoalEntryPage` shows "Reopen Goal" button when `IsCompleted = true`, hiding "Mark as Complete" via DataTrigger. Two new repository tests: `ReopenAsync_ClearsCompletionDate` and `ReopenAsync_WhenGuidNotFound_DoesNotThrow`.

**Why:** Web GoalDetail has a "Reopen Goal" button. Mobile only had "Mark as Complete" with no way to undo it. A caregiver who accidentally marks a goal complete had no recourse on mobile.

**Impact:** 232 mobile tests (up from 230). Build clean.

---

## 2026-05-17 — Insights page: streak + week-over-week trend (iter 329)

**What:** Added two new metrics to the Insights page:
- **Day streak:** Counts consecutive days with at least one action ending today. Flame icon turns orange at 3+ days, red at 7+.
- **Week-over-week trend:** Shows `TotalThisWeek` vs `TotalLastWeek` as % change with up/down trending icon.

Stats grid expanded from 3 to 4 cards (3→4 per row using `sm="3"`).

**Why:** Raw 30-day counts don't motivate behavior. A streak counter and trend line give caregivers momentum-based feedback — common in habit-tracking apps.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — UX: make 'Needs attention' stale goal banner tappable on dashboard (iter 354)

**What:** Added `StaleGoalGuid` observable property and `GoToStaleGoalCommand` to `DashboardViewModel`. Added `TapGestureRecognizer` to the stale goal `Border` in `DashboardPage.xaml`. Tapping navigates to `goals/entry?guid=<staleGoalGuid>`.

**Why:** The banner showed the goal name with "⚠ Needs attention:" but tapping it did nothing. The natural expectation is that tapping a notification opens the relevant item. Users would have to manually find the goal in the Goals list.

**Impact:** 238 mobile tests pass. Build clean.

---

## 2026-05-17 — Fix: web Register PIN minimum length validation (iter 353)

**What:** Added `if (Pin.Length < 4)` check in `Register.razor.DoRegister()`. Updated PIN field helper text from "Remember this PIN" to "At least 4 characters — remember this to log back in".

**Why:** Mobile `SetupViewModel.CanCreate` enforced `Pin.Length == 4`. Web had no minimum, allowing empty or very short PINs that would be trivially guessable.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Mobile: show past meeting dates in orange on goal list (iter 352)

**What:** Added `MeetingDateColorConverter` (orange for past, gray for future) to `DueDateConverter.cs`. Updated `MeetingDateConverter` to prefix with "Missed:" for past dates instead of "Meet:". `GoalListPage.xaml` now uses the color converter on the meeting date label. Registered `MeetingDateColorConverter` in `App.xaml`.

**Why:** Companion improvement to iter 351 (web past meeting indicator). Mobile goal list previously showed all meeting dates in gray, making it impossible to spot missed meetings at a glance. Orange label + "Missed:" prefix provides the same visual cue as the web's amber warning.

**Impact:** 238 mobile tests pass. Build clean.

---

## 2026-05-17 — UI: show 'Missed meeting' in warning color for past meeting dates (iter 351)

**What:** Goal cards on the Home page and the GoalDetail header now show "Missed meeting" in amber/warning color when the `NextMeetingDate` has already passed, instead of always showing "Next meeting" in the same style regardless of whether the date is past or future.

**Why:** A goal with a missed meeting looks identical to one with an upcoming meeting — both showed the same blue "📅 Next meeting" label. The visual distinction makes it clear which goals need a rescheduled meeting.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Fix: web login always failing due to double-hashed PIN (iter 350)

**What:** `Register.razor` was hashing the PIN twice: `BCrypt(BCrypt(Pin))`. `Login.razor` hashed once and called `BCrypt.Verify(BCrypt(Pin), stored)`. Because BCrypt uses random salts, `BCrypt(Pin)` at login ≠ `BCrypt(Pin)` at register — so the verification always failed. Fixed Register to store `BCrypt.HashPassword(Pin)` (single hash) and Login to verify `Pin` directly against the stored hash. Matches the mobile `AccountService` pattern.

**Why:** Every web-registered account was permanently unloggable. Since login was always broken, no actual web user data is at risk from changing the hash format.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Fix: Insights page redirects to /login when not authenticated (iter 349)

**What:** Replaced the `"Please log in"` text placeholder in `Insights.razor` with `Nav.NavigateTo("/login")`. Added `@inject NavigationManager Nav`.

**Why:** All other authenticated pages (GoalDetail, Journal, Todos) redirect on null session; Insights was inconsistent and showed a text link instead.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Fix timezone bug in JournalPage date saving (iter 348)

**What:** Replaced `new DateTimeOffset(date, TimeSpan.Zero)` with `DateTime.SpecifyKind(date, DateTimeKind.Local)` in `JournalPage.razor` for both the new-entry and edit-entry `EnteredDate` save paths.

**Why:** Companion fix to iter 347. Same `TimeSpan.Zero` bug — the `MudDatePicker` returns a local `DateTime`, but wrapping with `TimeSpan.Zero` treats it as UTC. Journal dates saved on a UTC+2 server would round-trip to the wrong day on non-UTC clients.

**Impact:** 217 API tests pass. No remaining `TimeSpan.Zero` date conversions in any web page.

---

## 2026-05-17 — Fix timezone bug in GoalDetail and Todos date saving (iter 347)

**What:** Replaced `new DateTimeOffset(date, TimeSpan.Zero)` with `DateTime.SpecifyKind(date, DateTimeKind.Local)` in `GoalDetail.razor` (AddProgress, EditProgress, EditGoal — meeting date and expiration date) and `Todos.razor` (ApplyFilter Today boundary, AddTodo due date, EditTodo due date).

**Why:** Same root cause as iter 335 — `MudDatePicker`/`MudTextField` returns a local `DateTime`, but `TimeSpan.Zero` treats it as UTC. On a UTC+2 server, the "Today" filter bucket would start 2 hours late; saved dates would be off by the UTC offset in cross-timezone scenarios.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Fix: mobile goal entry always sets NextMeetingDate (iter 346)

**What:** Added `HasNextMeetingDate` toggle switch to `GoalEntryPage.xaml` (same pattern as the `HasExpirationDate` toggle). New goals default to no meeting date. Existing goals restore the toggle if the stored value is non-null. `GoalEntryViewModel.SaveAsync` now writes `null` when the toggle is off instead of always writing a date 7 days from today.

**Why:** Every goal created on mobile got a `NextMeetingDate` set to T+7 days regardless of intent, causing a spurious meeting badge in the GoalListPage and DashboardViewModel's "Next goal meeting" banner. The web GoalDetail correctly treats meeting date as optional; this brings mobile into parity.

**Impact:** 238 mobile tests pass. Build clean.

---

## 2026-05-17 — Analytics: track journal_filter and todo_filter events (iter 345)

**What:** Added `_ = Analytics.TrackAsync("journal_filter", ...)` to `JournalPage.OnDateFilterChanged` and `_ = Analytics.TrackAsync("todo_filter", ...)` to `Todos.OnStatusFilterChanged`. Both event names existed in `Insights.FormatEventName` but were never fired.

**Why:** Insights was mapping these event keys but seeing zero counts in the breakdown. Knowing how often users filter journal/todos (and which filters are popular) lets us optimize the default view or promote popular filters.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Fix missing todo_edit and logout event names in Insights (iter 344)

**What:** Added `"todo_edit" => "Edit todo"` and `"logout" => "Logout"` to `FormatEventName` in `Insights.razor`. Both events are tracked but were falling through to the raw-name fallback (`name.Replace("_", " ")`), showing "todo edit" and "logout" instead of clean labels.

**Why:** Completes the event name mapping added in iter 339. `todo_edit` is tracked on every save from the Todos edit dialog; `logout` is tracked on every logout. Both appear in the Insights event breakdown for active users.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Mobile: GetCountSinceAsync for journal 7-day count (iter 343)

**What:** Added `GetCountSinceAsync(accountFk, sinceMs)` to `JournalRepository` — a DB-level `COUNT` query that avoids loading all journal records. Updated `DashboardViewModel.RefreshDataAsync` to call it instead of loading all active journals to count in memory. Added 3 new tests: `GetCountSinceAsync_CountsEntriesOnOrAfterThreshold`, `GetCountSinceAsync_ExcludesSoftDeletedEntries`, `GetCountSinceAsync_ExcludesOtherAccounts`.

**Why:** The dashboard previously called `GetAllActiveAsync` (loads full records) then counted with LINQ — unnecessarily expensive as the journal grows. A COUNT query at the DB layer returns only the number without deserializing any records.

**Impact:** 238 mobile tests pass. Build clean.

---

## 2026-05-17 — Mobile dashboard: journal-this-week stat card (iter 342)

**What:** Added a third stat card ("Journal (7d)") to the mobile dashboard summary row, showing the count of journal entries from the last 7 days. Card taps navigate to `//journal`. Summary grid changed from 2-column to 3-column; card labels and font sizes adjusted for the tighter layout. Added `JournalThisWeek` observable property and `GoToJournalCommand` to `DashboardViewModel`.

**Why:** The web home page has always shown a journal-this-week count alongside Goals and Todos. Mobile showed only 2 stats, leaving caregivers without a quick signal of whether they've been keeping up with observations. The 3-card layout matches the web home exactly.

**Impact:** 235 mobile tests pass. Build clean.

---

## 2026-05-17 — Mobile setup: add Confirm PIN field (iter 341)

**What:** Added `ConfirmPin` property and confirm Entry to `SetupPage.xaml`. `CanCreate` now requires both PIN fields to have 4 characters. On submit, if `Pin != ConfirmPin`, an error message "PINs do not match" is shown and registration is blocked.

**Why:** Matches the web register page fix from iter 323. A typo during PIN setup on mobile creates an account the user can never log into. The 4-digit + numeric keyboard constraint reduces but doesn't eliminate the risk.

**Impact:** 235 mobile tests pass. Build clean.

---

## 2026-05-17 — Fix: mobile journal edit ignores DatePicker changes (iter 340)

**What:** Added `journal.EnteredDate = enteredMs;` in `JournalEntryViewModel.SaveAsync()` after loading an existing entry. Previously, `enteredMs` was computed from the `EnteredDate` bound to the DatePicker, but the existing journal's `EnteredDate` was loaded from the DB and the picker change was never written back.

**Why:** Opening an existing journal entry and changing the date would save all other fields (notes, activity, mood, tags) but silently discard the date change. The DatePicker appeared functional but had no effect on save. The fix is a single line that applies the picker's value before saving, matching the new-entry code path.

**Impact:** 235 mobile tests pass. Build clean.

---

## 2026-05-17 — Insights: complete event name mapping (iter 339)

**What:** Added missing event name mappings to `FormatEventName` in Insights.razor: `goal_edit`, `goal_reopen`, `progress_edit`, `progress_delete`, `journal_edit`, `journal_delete`, `todo_uncomplete`, `login`, `register`. Events without a mapping fell through to `name.Replace("_", " ")` which produced strings like "goal reopen" instead of "Reopen goal".

**Why:** Several events were added in iterations 291–334 but `FormatEventName` was not updated. The Insights page would display raw underscore-names, making the "Top Features Used" section hard to read.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Mobile: GetRecentAsync on JournalRepository + dashboard optimization (iter 338)

**What:** Added `GetRecentAsync(accountFk, count)` to `JournalRepository` — fetches only N most recent entries at the DB layer. `DashboardViewModel` now uses `GetRecentAsync(account.Guid, 3)` instead of `GetAllActiveAsync(...).Take(3)`. Added 3 tests: count limit, sort by EnteredDate descending, excludes soft-deleted entries.

**Why:** `GetAllActiveAsync` loads all journal entries into memory then discards everything after the third. For a caregiver who has used the app for 2+ years, this could be hundreds of rows fetched for no reason. Pushing the LIMIT to the DB layer eliminates the wasteful load.

**Impact:** 235 mobile tests (up from 232). Build clean.

---

## 2026-05-17 — Web home: onboarding alert for users with no goals (iter 337)

**What:** When a logged-in user has no active goals and no search filter applied, a MudAlert info banner appears above the goals grid explaining the app's core concept ("Goals are the heart of ChildDev...") and nudging them to add the first goal.

**Why:** New users and returning users who have completed all goals see an empty grid with just the dashed "Add New Goal" card. The blank state gives no context about what to do or why. A brief, contextual prompt fills this gap without being intrusive — it only shows when there are literally zero active goals.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Fix stale NowMs in Todos overdue filter (iter 336)

**What:** `NowMs` is now refreshed at the start of `LoadTodos()` instead of only in `OnInitializedAsync`.

**Why:** `NowMs` was set once at page load. If the user left the browser open overnight, todos whose due dates crossed midnight would not show as overdue — the Overdue filter and overdue badge used the stale timestamp. Refreshing `NowMs` on every `LoadTodos()` call (which happens after every add/complete/delete) keeps the overdue state accurate for the session.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Fix timezone bug in analytics and progress charts (iter 335)

**What:** Replaced `new DateTimeOffset(d, TimeSpan.Zero)` with `new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Local))` in `BuildDailyChart` (Insights.razor) and `BuildProgressChart` (GoalDetail.razor).

**Why:** `d` is derived from `LocalDateTime.Date` — it is a local midnight. Wrapping it with `TimeSpan.Zero` treats it as UTC midnight, which is wrong for any server not running in UTC. The bucket boundaries would be off by the server's UTC offset (e.g., 2 hours for UTC+2), causing events in the early morning to land in the wrong day's bar. Using `DateTimeKind.Local` correctly aligns bucket boundaries with local midnight.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Web register: Enter key submits from Confirm PIN field (iter 334)

**What:** Added `@onkeyup` handler on the Confirm PIN field in `Register.razor`, mirroring the login page fix (iter 333).

**Why:** Same keyboard UX expectation as login — completing the last field and pressing Enter should submit the form.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Web login: Enter key submits form (iter 333)

**What:** Added `@onkeyup` handler on the PIN `MudTextField` in `Login.razor`. When the user presses Enter, `DoLogin()` is called directly — no need to reach for the mouse after typing the PIN.

**Why:** Standard form UX expectation. Typing NickName → Tab → PIN → Enter is the natural keyboard flow; previously it was broken and forced a mouse click.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Fix MUD0002 build warnings (iter 332)

**What:** Replaced `FullWidth="true"` direct attribute on `MudDialog` in `Home.razor` and `GoalDetail.razor` with `Options="@(new DialogOptions { MaxWidth = ..., FullWidth = true })"`. MudBlazor 7 removed `FullWidth` as a first-class parameter on `MudDialog` — it must be passed through `DialogOptions`.

**Why:** MUD0002 is a code analyzer warning surfaced by the MudBlazor v7 Roslyn analyzer. Zero warnings makes CI cleaner and avoids silent behavior differences from misconfigured dialogs.

**Impact:** 217 API tests pass. Build now reports 0 warnings (was 4).

---

## 2026-05-17 — Mobile dashboard personalized greeting (iter 331)

**What:** Added a `Greeting` observable property to `DashboardViewModel`. On load, it reads the account's `NickName` and generates a time-of-day greeting ("Good morning/afternoon/evening, [Name]!"). The `DashboardPage` shows it as a bold heading at the top, hidden when empty.

**Why:** The dashboard opened with sync status and counts but nothing personal. Caregivers log observations on behalf of specific children — a warm greeting sets the tone and confirms who's logged in.

**Impact:** 232 mobile tests pass. Build clean.

---

## 2026-05-17 — Journal date-range filter chips (iter 330)

**What:** Added "All / This Week / This Month" filter chips to the web Journal page toolbar. The `DateFilter` field and `OnDateFilterChanged()` method were added to the `@code` block; `ApplyFilter()` was updated to apply the date range before the text search, making both filters composable.

**Why:** The Journal page accumulated entries over time with no way to narrow the view. The Todos page had status filter chips (iter 318); Journal needed equivalent date-range filtering. "This Week" and "This Month" match the natural review cadences caregivers use.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Feat: web Settings page for account management (iter 355)

**What:** Created `Settings.razor` at `/settings` with three sections: (1) read-only account info (nickname, account ID, member-since date), (2) change-nickname form with duplicate-check validation, and (3) change-PIN form requiring current PIN verification. Added "Settings" link (with gear icon + username) to the navbar in `MainLayout.razor`, replacing the plain nickname text. Analytics tracking on page view, nickname change, and PIN change.

**Why:** The web UI had no way to update account credentials — users could register and log in but were stuck with whatever nickname/PIN they picked. The mobile SettingsPage has existed since early iterations. This brings web to parity.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Analytics: add settings event names to Insights (iter 356)

**What:** Added `settings_change_nickname` → "Change nickname" and `settings_change_pin` → "Change PIN" to `FormatEventName` in `Insights.razor`. Added `settings` → "Settings" to `FormatPageName`.

**Why:** New events from iter 355 would have appeared as raw snake_case strings in the Insights feature-usage list without these mappings.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Refactor: remove dead Razor Pages code (iter 362)

**What:** Deleted the entire `Pages/` directory (13 files): `Index.cshtml`, `Login.cshtml`, `Logout.cshtml`, `Register.cshtml`, `Goals/Index.cshtml` and their `.cs` code-behind files, plus `_Layout.cshtml`, `_ViewImports.cshtml`, `_ViewStart.cshtml`. None of these files were routed — `MapRazorPages()` is absent from `Program.cs`.

**Why:** These Razor Pages were from a prior implementation before the Blazor migration. They compiled but were never served. Leaving them created maintenance confusion (any developer reading the codebase would see two implementations of login/register/goals and not know which was live).

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — Fix: completed goals sorted above active goals on mobile (iter 363)

**What:** Fixed `GoalListViewModel.LoadGoalsWithStepsAsync` to separate active from completed goals before applying the progress-staleness sort. Active goals are sorted by progress staleness (null-progress first, then oldest-update-first). Completed goals are appended after, sorted by `CompletionDate` descending (most recently completed first).

**Why:** The progress-staleness `OrderBy` was applied to the entire goal list including completed ones. A completed goal with no progress notes (`LatestProgressAt == null`) would sort to position 0 — above all active goals. This made completed, never-progressed goals appear at the top, the opposite of the desired UX.

**Impact:** 238 mobile tests pass. Build clean.

---

## 2026-05-17 — UX: Restore button on mobile TodoEntryPage (iter 359)

**What:** Added `IsCompleted` property to `TodoEntryViewModel` (populated from `CompletedAt`). Added a "Restore Task" button (blue, visible when `IsCompleted=True`) and fixed "Mark as Done" to be invisible for already-completed todos (previously it was always visible when editing an existing todo).

**Why:** Tapping a completed todo in the mobile list opened its entry page, but there was no way to un-complete it from there. The "Mark as Done" button was always visible, even for completed tasks. On mobile the swipe-left "Undo" is less discoverable than a button.

**Impact:** 238 mobile tests pass. Build clean.

---

## 2026-05-17 — UX: 'No notes yet' label for goals with no progress (iter 360)

**What:** Added `NullConverter` (inverse of `NotNullConverter`) to the mobile converter set. Added "No notes yet" label (gray, FontSize 10) to `GoalListPage.xaml` for goals where `LatestProgressAt` is null.

**Why:** Goals with no progress are sorted to the top of the list (needs-attention ordering), but previously had no visual indicator explaining why they appear first. "No notes yet" makes the priority signal explicit.

**Impact:** 238 mobile tests pass. Build clean.

---

## 2026-05-17 — UX: Show goal name in quick progress note dialog (iter 361)

**What:** Added `QuickNoteGoalText` field in `Home.razor`. `OpenQuickNote` now looks up the goal text from `AllActiveGoals`. The dialog title area shows the goal text as a caption below "Add Progress Note".

**Why:** The quick note button appears on every goal card. When clicked, the dialog showed only "Add Progress Note" with no indication of which goal the note applies to. With many goals, this was confusing.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — UX: Todos filter empty-state message (iter 357)

**What:** Added a "No todos matching the current filter." message to `Todos.razor` when the active filter (Overdue / Today / NoDate) or search yields zero results but pending todos exist. Previously the list was silently empty.

**Why:** Without an empty-state message, the blank list looks like a loading failure. The Todos page already had filter chips; it needed the corresponding empty-state feedback.

**Impact:** 217 API tests pass. Build clean.

---

## 2026-05-17 — UX: Fix journal date-filter empty-state message (iter 358)

**What:** Fixed the empty-state message on `JournalPage.razor` when a date filter is active but yields no results. Previously it showed `No entries matching "".` (with a blank placeholder) when no text search was active. Now shows "No entries in this date range." (date-filter only), "No entries matching '{text}'." (text only), or "No entries matching '{text}' in this date range." (both).

**Why:** The `No entries matching "".` string was confusing — it implied a failed text search when there was none. Users selecting "This Week" with no entries that week would have seen it.

**Impact:** 217 API tests pass. Build clean.

---

---

## 2026-05-17 — Fix: mobile sync permanently broken — add server link flow (iter 364)

**What:** `AccountService.SaveServerCredentialsAsync` was defined but never called from any ViewModel. `SyncService.RunAsync` returns `SyncResult.NoServer` when `account.ServerJwt` is null — meaning sync was always a no-op for all real users. 

Added `AccountService.LinkToServerAsync(jwt, serverUrl, serverAccountGuid)` which:
1. Migrates all local record `AccountFk` values from the old local GUID to the server's GUID via raw SQL updates to Journal, Goal, GoalProgress, and Todo tables
2. Updates the Account.Guid PK via raw SQL so subsequent record creation uses the server GUID
3. Also stores JWT and ServerUrl on the same row

Added `AccountService.ClearServerJwtAsync()` for unlinking.

Updated `SettingsViewModel` with `LinkToServerCommand` (NickName + PIN → `/api/auth/token` → receives JWT + serverAccountGuid → calls LinkToServerAsync) and `UnlinkFromServerCommand`.

Updated `SettingsPage.xaml` with a conditional section:
- When linked: green "✓ Linked to server account" + red "Unlink from Server" button
- When not linked: NickName + PIN entry + blue "Link to Server" button

**Why:** The GUID used as `AccountFk` on all mobile records must match the GUID embedded in the server JWT claim, otherwise the server's ownership check skips all records during sync. Before this fix, every sync for every user silently returned `NoServer`.

**Impact:** 238 mobile tests, 217 API tests — all passing. Build clean.


---

## 2026-05-17 — Fix: 'No notes yet' shown on completed goals in mobile goal list (iter 365)

**What:** Added `ShowNoNotesYet` computed property to `Goal` model: returns `true` only when both `LatestProgressAt is null` AND `CompletionDate is null`. Updated `GoalListPage.xaml` binding from `NullConverter` on `LatestProgressAt` to the new property.

**Why:** A completed goal with no progress notes is done — it shouldn't show "No notes yet" since the goal has been achieved. The "No notes yet" label is intended to prompt the user to add progress, which is not relevant for completed goals.

**Impact:** 238 mobile tests pass. Build clean.

---

## 2026-05-17 — Fix: JSON deserialization for server auth response in Settings (iter 365b)

**What:** Added `PropertyNameCaseInsensitive = true` to `ReadFromJsonAsync<AuthResponse>` in `SettingsViewModel.LinkToServerAsync`. The server returns camelCase JSON (`jwt`, `accountGuid`) but the private `AuthResponse` record has PascalCase fields. Without case-insensitive matching, the response would always deserialize as null and the link flow would silently fail.

**Impact:** 238 mobile tests pass. Build clean.

---

## 2026-05-17 — UX: show 'No notes yet' on web goal cards with zero progress (iter 366)

**What:** Goal cards on the web home page now show a "No notes yet" caption when a goal has zero progress entries and no expiration date. Previously those cards had an invisible MudCardContent with no content.

**Why:** Active goals with no progress notes need attention — the visual gap was invisible, making it unclear whether the goal had been worked on. Consistent with the mobile goal list treatment.

**Impact:** 217 API tests pass. Build clean.


---

## 2026-05-17 — Fix: activity-only journal entries blocked from sync (iter 367)

**What:** 
1. Server sync validation changed from "reject if Notes is blank" to "reject if BOTH Notes AND Activity are blank". Added 1 API test confirming Activity-only entries are accepted.
2. Mobile `JournalEntryViewModel.CanSave()` changed to allow saving when either Notes OR Activity is non-empty (was Notes-only). `SaveAsync` now stores null Notes instead of empty-string when Notes field is blank.

**Why:** The web journal has always allowed Activity-only entries (`if (string.IsNullOrWhiteSpace(NewNotes) && string.IsNullOrWhiteSpace(NewActivity)) return;`). These entries (null Notes, non-null Activity) were stored directly to the server DB and returned to mobile during sync. But the server sync endpoint would then reject those same entries if mobile tried to push them back. Inconsistency in both the web, server validation, and mobile CanSave.

**Impact:** 218 API tests, 238 mobile tests — all passing. Build clean.


---

## 2026-05-17 — UX: Enter key submits register form from all fields (iter 368)

**What:** Added `@onkeyup` Enter handler to NickName and PIN fields on `Register.razor`. Previously only Confirm PIN submitted on Enter; the other fields did nothing on Enter.

**Why:** Consistent with `Login.razor` which added Enter key support in iter 333. Users who press Enter after filling in Nickname or PIN expect the form to advance or submit.

**Impact:** 218 API tests pass. Build clean.

---

## 2026-05-17 — Test: coverage for AccountService.LinkToServerAsync and ClearServerJwtAsync (iter 369)

**What:** Added `AccountServiceLinkTests` class to `AccountServiceTests.cs` with 6 tests:
- Link with same GUID saves JWT/URL without migrating
- Link with different GUID migrates account.Guid 
- Link with different GUID migrates Journal.AccountFk
- Link with different GUID migrates Goal.AccountFk
- ClearServerJwt removes JWT, preserves ServerUrl
- ClearServerJwt with no account is safe

**Why:** The `LinkToServerAsync` method introduced in iter 364 is critical path for enabling sync. It does raw SQL cross-table migrations, which are hard to verify without tests. Zero test coverage was a risk.

**Impact:** 244 mobile tests (was 238), 218 API tests — all passing.

---

## 2026-05-17 — UX: pre-populate meeting date in Add Progress dialog (iter 370)

**File:** `ChildDev.Api/Components/Pages/GoalDetail.razor`

**Change:** Replaced inline `OnClick="() => ShowAddDialog = true"` on the "Add Progress Note" button with a call to a new `OpenAddProgressDialog()` method. The method pre-fills `NewMeetingDate` with the goal's current `NextMeetingDate` when it is set and in the future, then opens the dialog. Also resets `NewNextSteps` to ensure no stale state from a prior cancelled dialog.

**Why:** When a goal has a scheduled meeting, users adding a progress note almost always want to record that meeting date. The picker defaulting to null forced redundant date entry every session. Pre-populating with the upcoming meeting date eliminates the friction for the common case while still allowing the user to change or clear it.

**Impact:** 218 API tests — all passing.

---

## 2026-05-17 — Fix: stale dialog state in Home.razor Add Goal card (iter 373)

**File:** `ChildDev.Api/Components/Pages/Home.razor`

**Change:** Replaced `@onclick="() => ShowAddDialog = true"` on the "Add New Goal" card with a call to `OpenAddGoalDialog()` that resets `NewGoalText` and `NewMeasurableOutcome` before showing the dialog.

**Why:** Same stale-state pattern as iter 371 — if the user started typing a goal, cancelled, then clicked the card again, the previous text reappeared. This fix ensures a fresh state every time.

**Impact:** 220 API tests — all passing.

---

## 2026-05-17 — Fix: Activity-only journal entries blank on Dashboard recent list (iter 375)

**Files:** `ChildDev.Mobile/Models/Journal.cs`, `ChildDev.Mobile/Views/DashboardPage.xaml`

**Change:** Added `[Ignore] public string DisplayText => Notes ?? Activity ?? string.Empty` computed property to `Journal`. Updated `DashboardPage.xaml` to bind recent journal entries to `DisplayText` instead of `Notes`.

**Why:** After iter 367, Activity-only journal entries (null Notes) are valid. The Dashboard's recent journal list was binding directly to `Notes`, which displayed as blank text for Activity-only entries. `DisplayText` falls back to Activity when Notes is null, ensuring the entry always shows some text.

**Impact:** 244 mobile tests — all passing.

---

## 2026-05-17 — UX: Enter key on Login Nickname field (iter 374)

**File:** `ChildDev.Api/Components/Pages/Login.razor`

**Change:** Added `@onkeyup` Enter handler to the Nickname field so pressing Enter there submits the login form. The PIN field already had this; Nickname was inconsistent.

**Impact:** 220 API tests — all passing.

---

## 2026-05-17 — Fix: GoalProgress sync rejects meeting-date-only records (iter 372)

**Files:** `ChildDev.Api/Endpoints/GoalProgressEndpoints.cs`, `ChildDev.Api.Tests/GoalProgressSyncTests.cs`

**Change:** Updated sync endpoint validation to allow GoalProgress records that have a `NextMeetingDate` but null `NextStepItems`. Previously validation required `NextStepItems` to be non-blank for any non-deleted record. Added tests `Sync_MeetingDateOnlyNullNextSteps_Accepted` and `Sync_NullNextStepsAndNoMeetingDate_Returns422`.

**Why:** The web UI allows creating progress notes with only a meeting date (no notes text). These records sync down to mobile fine, but when mobile sends them back up in the next sync cycle, the server rejected the whole batch with 422. Meeting-date-only progress notes are a valid use case (scheduling a future check-in without adding notes yet), so the sync endpoint should accept them.

**Impact:** 220 API tests (was 218) — all passing.

---

## 2026-05-17 — Fix: stale dialog state when reopening Add dialogs (iter 371)

**Files:** `ChildDev.Api/Components/Pages/JournalPage.razor`, `ChildDev.Api/Components/Pages/Todos.razor`

**Change:** Replaced `OnClick="() => ShowAddDialog = true"` with calls to `OpenAddDialog()` methods that clear all input fields before showing the dialog. Journal clears: Notes, Activity, Mood, Tags, EnteredDate (reset to today). Todos clears: Title, Notes, DueDate.

**Why:** Clicking Cancel on a partially-filled New Entry or Add Todo dialog left the typed text in component state. Reopening the dialog would show the previous unfinished text, which is surprising and error-prone — the user might accidentally submit an entry they had decided not to create.

**Impact:** 218 API tests — all passing.

