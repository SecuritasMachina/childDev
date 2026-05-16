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

## Flagged but not implemented (requires backend coordination)

**Password in URL:** `account.service.ts` `token()` method sends the password as a plain path segment in a GET request (`/token/{nickname}/{password}`). Passwords in URLs are logged by servers, proxies, and browser history. Fix requires a POST-based authentication endpoint on the backend.

---

## Pre-existing lint errors (not introduced by this session)

Multiple `azAuthHeader` quoting, line-length, and semicolon issues across `goal.service.ts`, `journal.service.ts`, and `todo.service.ts`. These pre-date this session and are cosmetic — tracked for a future focused lint-cleanup pass.
