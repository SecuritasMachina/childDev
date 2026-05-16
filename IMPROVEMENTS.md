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

## Flagged but not implemented (requires backend coordination)

**Password in URL:** `account.service.ts` `token()` method sends the password as a plain path segment in a GET request (`/token/{nickname}/{password}`). Passwords in URLs are logged by servers, proxies, and browser history. Fix requires a POST-based authentication endpoint on the backend.

---

## Pre-existing lint errors (not introduced by this session)

Multiple `azAuthHeader` quoting, line-length, and semicolon issues across `goal.service.ts`, `journal.service.ts`, and `todo.service.ts`. These pre-date this session and are cosmetic — tracked for a future focused lint-cleanup pass.
