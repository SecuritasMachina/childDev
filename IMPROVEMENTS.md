# Improvement Log

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

## Flagged but not implemented (requires backend coordination)

**Password in URL:** `account.service.ts` `token()` method sends the password as a plain path segment in a GET request (`/token/{nickname}/{password}`). Passwords in URLs are logged by servers, proxies, and browser history. Fix requires a POST-based authentication endpoint on the backend.

---

## Pre-existing lint errors (not introduced by this session)

Multiple `azAuthHeader` quoting, line-length, and semicolon issues across `goal.service.ts`, `journal.service.ts`, and `todo.service.ts`. These pre-date this session and are cosmetic — tracked for a future focused lint-cleanup pass.
