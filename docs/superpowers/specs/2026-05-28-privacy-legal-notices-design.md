# Design: Privacy Notices & Legal Pages for Google Play Children's App

**Date:** 2026-05-28
**Status:** Approved
**Legal Entity:** Acknowledged Development Inc.
**App:** LevelUp — levelup.securitasmachina.org
**Laws addressed:** COPPA (US), CCPA/CPRA (California), Google Play Families Policy

---

## 1. Context & Compliance Strategy

LevelUp targets children ages 5–18 and their caregivers. The chosen COPPA model is **Account Holder = Adult (13+)**:
- Account holders must be 13 or older
- Children use the app under the adult account holder's authority
- No separate parental consent mechanism is needed beyond the registration acknowledgment
- This is the same model used by Duolingo Kids, Headspace for Kids, and most indie children's tools

Third-party data sharing: none. No advertising SDKs, no analytics platforms, no crash reporters. Only the app's own server and device-local storage.

Account deletion already exists at Settings → Delete My Account (purges all data). This satisfies the CCPA and COPPA deletion right mechanically.

---

## 2. Architecture

Five deliverables, all within the existing Blazor Server + MudBlazor stack:

| Deliverable | Route | Description |
|---|---|---|
| Privacy Policy | `/privacy` | Static Blazor page, no auth required |
| Terms of Service | `/terms` | Static Blazor page, no auth required |
| Privacy Contact Form | `/privacy/contact` | Form page with server-side email send |
| `PrivacyContactService` | — | Singleton: rate limiting + email dispatch |
| Footer/nav link updates | Home, Register, About, Settings, MainLayout | Adds Privacy / Terms links |

No new database tables. Contact form submissions are emailed to the operator; the inbox is the audit trail.

---

## 3. Privacy Policy (`/privacy`)

**Effective date:** displayed dynamically from a constant in `BuildInfo.cs` or hardcoded.

### Sections

**1. Who We Are**
Acknowledged Development Inc., `privacy@securitasmachina.org`, link to `/privacy/contact`, effective date.

**2. Who This App Is For**
Account holders must be 13 or older. Parents and caregivers create accounts on behalf of children ages 5–18. Children use LevelUp under an adult account holder's authority. This is the COPPA "parent/guardian as account holder" model — no separate verifiable parental consent mechanism is required.

**3. What We Collect**

*You provide:*
- Nickname (display name, not a real name requirement)
- PIN (stored as a one-way BCrypt hash — never readable)
- Goals, progress notes, journal entries, to-do tasks, reminders

*We generate automatically:*
- Anonymous usage events (e.g., "goal_create", "journal_view") tied to your account ID
- No device identifiers, no IP address stored with events, no advertising profiles

**4. How We Use It**
- Operate and sync the app across your devices
- Improve features based on aggregate, anonymized usage patterns
- No selling of data. No advertising. No sharing with third parties.

**5. Data Storage & Security**
- Data stored on our servers (United States) and on your device
- PIN is BCrypt-hashed and never transmitted in readable form
- Sync is authenticated via JWT tokens
- We implement reasonable security measures; no system is perfectly secure

**6. Your Rights (COPPA + CCPA)**
All users:
- Right to know what data is collected (this policy)
- Right to access your data (contact us)
- Right to delete your account and all data (Settings → Delete My Account, or `/privacy/contact`)
- Right to correct inaccurate data (contact us)

California residents additionally have rights under CCPA/CPRA including the right to opt out of the "sale" of personal information (we do not sell data) and the right to limit use of sensitive personal information (we use it only to operate the service).

Parents and guardians may request review or deletion of a child's account data via `/privacy/contact`.

**7. Data Retention**
Data is kept until you delete your account. Deleted accounts and all associated data are purged from our servers within 30 days.

**8. Children's Privacy (COPPA)**
We do not knowingly collect personal information from children under 13 without a parent or guardian as the account holder. If we learn that personal information has been collected from a child under 13 without appropriate parental authority, we will delete it promptly. To report a concern, contact us at `privacy@[domain]` or via `/privacy/contact`.

**9. Changes to This Policy**
We will update the effective date for any changes. Material changes will be noted on this page.

**10. Contact Us**
`privacy@securitasmachina.org` · `/privacy/contact`

---

## 4. Terms of Service (`/terms`)

**Sections**

**1. Acceptance**
Using LevelUp means you agree to these terms. If creating an account for a child, you agree on their behalf.

**2. Account Requirements**
Account holders must be 13 or older. You are responsible for keeping your PIN secure and for all activity under your account.

**3. What LevelUp Is**
A free goal-tracking tool provided as-is. No guarantees of uptime or feature availability. Not a substitute for professional therapeutic, educational, or medical services. LevelUp is not affiliated with any IEP process or official educational program.

**4. Your Content**
You own your goals, journal entries, and notes. You grant Acknowledged Development Inc. a limited license to store and sync your content solely to operate the service. We do not use your content for any other purpose.

**5. Acceptable Use**
- Do not store illegal content
- Do not attempt to reverse-engineer, scrape, or abuse the service
- Do not impersonate others

**6. Account Deletion**
Delete your account and all data at any time via Settings → Delete My Account. Server data is purged within 30 days.

**7. Disclaimer & Limitation of Liability**
Service provided as-is without warranty. Acknowledged Development Inc. is not liable for data loss or service interruptions. As LevelUp is a free service, liability is capped at zero dollars ($0).

**8. Changes**
We may update these terms. Material changes will be noted with an updated effective date.

**9. Governing Law**
These terms are governed by the laws of the State of California, United States.

**10. Contact**
`privacy@securitasmachina.org` · `/privacy/contact`

---

## 5. Privacy Contact Form (`/privacy/contact`)

### Form Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| Your name | text | yes | |
| Email address | email | yes | Used to respond |
| Request type | dropdown | yes | See options below |
| Account nickname | text | no | Helps locate the account |
| Message | textarea | yes | 2000 char max |
| Confirmation checkbox | checkbox | yes | "I confirm I am the account holder or parent/guardian of the account holder" |
| `website` | hidden honeypot | — | Rendered `display:none`; non-empty = bot |

**Request type options:**
- Delete my account and all data
- Access / export my data
- Question about privacy practices
- Parental request regarding a child's account
- Other

### Anti-Spam (no CAPTCHA)

Three server-side checks, all checked before the email is sent. Failed checks return a generic "please try again" message with no detail:

1. **Honeypot** — hidden `website` field must be empty. Bots fill it; humans never see it.
2. **Timing check** — a server-stamped token (HMAC of timestamp) is embedded in the form on render. Submissions arriving in under 4 seconds are rejected.
3. **Per-IP rate limit** — one successful submission per IP per 60 seconds, tracked in `PrivacyContactService` as `Dictionary<string, DateTime>` (in-memory singleton, no DB). Repeat submitters within the window see: "Your request was received. Please wait before submitting another."

### Submission Handling

On valid submission:
1. `PrivacyContactService.SendAsync()` sends an email to `Privacy:ContactEmailTo` (appsettings.json)
2. Subject: `[LevelUp Privacy] {RequestType} – nickname: {Nickname}`
3. Body: plain text with all fields + UTC timestamp
4. User sees confirmation: *"We received your request and will respond within 45 days as required by law."*
5. For "Delete my account" request type, confirmation also shows direct link to Settings → Delete My Account for immediate self-service

### Email Implementation

Reuses the existing `EmailService` (already registered, uses `CHILDDEV_SMTP_*` env vars). A new method `SendPrivacyRequestAsync(string requestType, string name, string email, string nickname, string message)` is added to `EmailService`. The `PrivacyContactService` injects `EmailService` and calls it on valid submission.

The operator recipient address is read from env var `CHILDDEV_PRIVACY_EMAIL` (falls back to `privacy@securitasmachina.org` if unset). No new SMTP configuration — existing infrastructure is reused.

If SMTP is not configured, the form logs the submission to the server log as a fallback (with warning).

---

## 6. Navigation Integration

### Register Page
Two required checkboxes added above the submit button (both unchecked by default; submit button disabled until both checked):

```
☐ I am 13 or older, or I am a parent/guardian creating this account on behalf of a child
☐ I have read and agree to the Privacy Policy and Terms of Service
```

Both policy links open in a new tab so the user doesn't lose registration progress.

### Home Page Footer
Add `Privacy Policy` and `Terms of Service` links.

### About Page
Add footer line: "Legal: Privacy Policy · Terms of Service · Contact / Privacy Requests"

### Settings Page
Under the Delete My Account section, add:
> "You can also submit a privacy request or ask questions at Privacy Contact →"

### MainLayout
Add `Privacy` and `Terms` links to the footer that appears on every authenticated page.

### Mobile App
No changes. Google Play references the web URLs. Mobile deep-links to policies can be added later.

---

## 7. Google Play Store Data Safety Form Answers

Reference when filling out the Play Console Data Safety section:

| Question | Answer |
|---|---|
| Does your app collect or share user data? | Yes |
| Data types collected | App activity (in-app actions), App info and performance |
| Is data collected encrypted in transit? | Yes |
| Can users request data deletion? | Yes |
| Do you share data with third parties? | No |
| Is this app directed at children? | Yes — Mixed audience (children and adults) |
| Does app include ads? | No |

Privacy policy URL for the store listing: `https://levelup.securitasmachina.org/privacy`

---

## 8. Files to Create / Modify

**New:**
- `ChildDev.Api/Components/Pages/Privacy.razor`
- `ChildDev.Api/Components/Pages/Terms.razor`
- `ChildDev.Api/Components/Pages/PrivacyContact.razor`
- `ChildDev.Api/Services/PrivacyContactService.cs`

**Modified:**
- `ChildDev.Api/Components/Pages/Register.razor` — add consent checkboxes
- `ChildDev.Api/Components/Pages/Home.razor` — add footer links
- `ChildDev.Api/Components/Pages/About.razor` — add legal footer
- `ChildDev.Api/Components/Pages/Settings.razor` — add privacy contact link
- `ChildDev.Api/Components/Layout/MainLayout.razor` — add footer links
- `ChildDev.Api/Services/EmailService.cs` — add `SendPrivacyRequestAsync` method
- `ChildDev.Api/Program.cs` — register `PrivacyContactService` as singleton
