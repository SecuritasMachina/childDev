# Privacy & Legal Notices Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add COPPA/CCPA-compliant Privacy Policy, Terms of Service, and Privacy Contact Form pages so LevelUp can be published on the Google Play Store as a children's app.

**Architecture:** Three new Blazor pages (`/privacy`, `/terms`, `/privacy/contact`) backed by a `PrivacyContactService` singleton that enforces honeypot, timing, and per-IP rate-limit checks before emailing form submissions via the existing `EmailService`. Register page gets two required consent checkboxes. Footer links are added to MainLayout, About, and Settings.

**Tech Stack:** Blazor Server, MudBlazor, `System.Net.Mail.SmtpClient`, xUnit (existing `ChildDev.Api.Tests` project), ASP.NET Core DI.

---

## File Map

**New files:**
- `ChildDev.Api/Services/PrivacyContactService.cs` — spam checks + email dispatch
- `ChildDev.Api/Components/Pages/Privacy.razor` — Privacy Policy (`/privacy`)
- `ChildDev.Api/Components/Pages/Terms.razor` — Terms of Service (`/terms`)
- `ChildDev.Api/Components/Pages/PrivacyContact.razor` — Contact form (`/privacy/contact`)
- `ChildDev.Api.Tests/PrivacyContactServiceTests.cs` — unit tests for service logic

**Modified files:**
- `ChildDev.Api/Services/EmailService.cs` — add `SendPrivacyRequestAsync`
- `ChildDev.Api/Program.cs` — register `PrivacyContactService` as singleton
- `ChildDev.Api/Components/Pages/Register.razor` — consent checkboxes
- `ChildDev.Api/Components/Layout/MainLayout.razor` — footer links
- `ChildDev.Api/Components/Pages/About.razor` — legal footer section
- `ChildDev.Api/Components/Pages/Settings.razor` — privacy contact link

---

## Task 1: Add `SendPrivacyRequestAsync` to `EmailService`

**Files:**
- Modify: `ChildDev.Api/Services/EmailService.cs`

- [ ] **Step 1: Write the failing test**

Create `ChildDev.Api.Tests/PrivacyContactServiceTests.cs` with a test for the no-SMTP path:

```csharp
using ChildDev.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChildDev.Api.Tests;

public class EmailServicePrivacyTests
{
    [Fact]
    public async Task SendPrivacyRequestAsync_NoSmtpConfigured_DoesNotThrow()
    {
        var config = new ConfigurationBuilder().Build(); // empty — no SMTP
        var svc = new EmailService(config, NullLogger<EmailService>.Instance);
        var request = new PrivacyContactRequest(
            Name: "Alice",
            Email: "alice@example.com",
            RequestType: "Question about privacy practices",
            Nickname: "alice",
            Message: "Hello",
            Website: "",
            ElapsedSeconds: 5);

        var ex = await Record.ExceptionAsync(() => svc.SendPrivacyRequestAsync(request));
        Assert.Null(ex);
    }
}
```

- [ ] **Step 2: Run test to confirm it fails (method doesn't exist yet)**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/levelUp
dotnet test ChildDev.Api.Tests/ChildDev.Api.Tests.csproj --filter "EmailServicePrivacyTests" -q 2>&1 | tail -5
```

Expected: build error — `PrivacyContactRequest` and `SendPrivacyRequestAsync` not defined.

- [ ] **Step 3: Add `PrivacyContactRequest` record and `SendPrivacyRequestAsync` to `EmailService`**

At the bottom of `ChildDev.Api/Services/EmailService.cs`, add:

```csharp
    public async Task SendPrivacyRequestAsync(PrivacyContactRequest request)
    {
        var privacyTo = _privacyEmail;
        var subject = $"[LevelUp Privacy] {request.RequestType} – nickname: {request.Nickname}";
        var body = $"""
            Privacy request received at {DateTime.UtcNow:u}

            Name:           {request.Name}
            Email:          {request.Email}
            Request Type:   {request.RequestType}
            Account Nickname: {request.Nickname}

            Message:
            {request.Message}
            """;

        if (string.IsNullOrWhiteSpace(_host) || string.IsNullOrWhiteSpace(_user))
        {
            logger.LogWarning(
                "SMTP not configured — privacy request not emailed. Type: {Type}, From: {Email}",
                request.RequestType, request.Email);
            return;
        }

        try
        {
#pragma warning disable SYSLIB0006
            using var client = new SmtpClient(_host, _port)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(_user, _pass)
            };
#pragma warning restore SYSLIB0006
            using var message = new MailMessage(_from ?? _user!, privacyTo, subject, body);
            await client.SendMailAsync(message);
            logger.LogInformation("Privacy request email sent. Type: {Type}", request.RequestType);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send privacy request email.");
        }
    }
```

Also add the `_privacyEmail` field and `PrivacyContactRequest` record. In `EmailService`, add to the existing field declarations:

```csharp
    private readonly string _privacyEmail = config["CHILDDEV_PRIVACY_EMAIL"] ?? "privacy@securitasmachina.org";
```

At the bottom of the file (outside the class), add:

```csharp
public record PrivacyContactRequest(
    string Name,
    string Email,
    string RequestType,
    string Nickname,
    string Message,
    string Website,       // honeypot field — must be empty
    double ElapsedSeconds // seconds since form rendered
);
```

- [ ] **Step 4: Run test to confirm it passes**

```bash
dotnet test ChildDev.Api.Tests/ChildDev.Api.Tests.csproj --filter "EmailServicePrivacyTests" -q 2>&1 | tail -5
```

Expected: `Passed! — Failed: 0, Passed: 1`

- [ ] **Step 5: Commit**

```bash
git add ChildDev.Api/Services/EmailService.cs ChildDev.Api.Tests/PrivacyContactServiceTests.cs
git commit -m "feat: add SendPrivacyRequestAsync and PrivacyContactRequest to EmailService"
```

---

## Task 2: `PrivacyContactService` + Registration

**Files:**
- Create: `ChildDev.Api/Services/PrivacyContactService.cs`
- Modify: `ChildDev.Api/Program.cs`
- Modify: `ChildDev.Api.Tests/PrivacyContactServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `ChildDev.Api.Tests/PrivacyContactServiceTests.cs`:

```csharp
public class PrivacyContactServiceTests
{
    private static PrivacyContactRequest ValidRequest(string ip = "1.2.3.4") => new(
        Name: "Test User",
        Email: "test@example.com",
        RequestType: "Question about privacy practices",
        Nickname: "testuser",
        Message: "This is my message with enough content.",
        Website: "",
        ElapsedSeconds: 5.0);

    private static PrivacyContactService BuildService()
    {
        var config = new ConfigurationBuilder().Build();
        var emailSvc = new EmailService(config, NullLogger<EmailService>.Instance);
        return new PrivacyContactService(emailSvc, NullLogger<PrivacyContactService>.Instance);
    }

    [Fact]
    public async Task Submit_HoneypotFilled_ReturnsSilentNull()
    {
        // Bot-filled honeypot: silent null so bot thinks it succeeded
        var svc = BuildService();
        var req = ValidRequest() with { Website = "http://spam.example.com" };
        var result = await svc.SubmitAsync(req, "10.0.0.1");
        Assert.Null(result);
    }

    [Fact]
    public async Task Submit_SubmittedTooFast_ReturnsErrorMessage()
    {
        var svc = BuildService();
        var req = ValidRequest() with { ElapsedSeconds = 2.0 };
        var result = await svc.SubmitAsync(req, "10.0.0.2");
        Assert.NotNull(result);
        Assert.Contains("moment", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_SameIpTwiceWithin60Seconds_ReturnsRateLimitMessage()
    {
        var svc = BuildService();
        await svc.SubmitAsync(ValidRequest(), "10.0.0.3"); // first — succeeds
        var second = await svc.SubmitAsync(ValidRequest(), "10.0.0.3");
        Assert.NotNull(second);
        Assert.Contains("wait", second, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_DifferentIps_BothSucceed()
    {
        var svc = BuildService();
        var first = await svc.SubmitAsync(ValidRequest(), "10.0.0.4");
        var second = await svc.SubmitAsync(ValidRequest(), "10.0.0.5");
        Assert.Null(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task Submit_ValidRequest_ReturnsNull()
    {
        var svc = BuildService();
        var result = await svc.SubmitAsync(ValidRequest(), "10.0.0.6");
        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail (class doesn't exist)**

```bash
dotnet test ChildDev.Api.Tests/ChildDev.Api.Tests.csproj --filter "PrivacyContactServiceTests" -q 2>&1 | tail -5
```

Expected: build error — `PrivacyContactService` not defined.

- [ ] **Step 3: Create `PrivacyContactService.cs`**

```csharp
using ChildDev.Api.Services;

namespace ChildDev.Api.Services;

public class PrivacyContactService(EmailService email, ILogger<PrivacyContactService> logger)
{
    private readonly Dictionary<string, DateTime> _recentSubmissions = new();
    private readonly Lock _lock = new();

    // Returns null on success; returns an error message string on rejection.
    // Honeypot failures return null (silent — bot thinks it succeeded).
    public async Task<string?> SubmitAsync(PrivacyContactRequest request, string? ipAddress)
    {
        // 1. Honeypot: non-empty means a bot filled the hidden field
        if (!string.IsNullOrEmpty(request.Website))
        {
            logger.LogWarning("Privacy form honeypot triggered from IP {IP}", ipAddress);
            return null; // silent rejection
        }

        // 2. Timing check: humans can't read and fill a form in under 4 seconds
        if (request.ElapsedSeconds < 4.0)
        {
            logger.LogWarning("Privacy form submitted too fast ({Elapsed:F1}s) from IP {IP}",
                request.ElapsedSeconds, ipAddress);
            return "Please take a moment to review your request and try again.";
        }

        // 3. Per-IP rate limit: one submission per IP per 60 seconds
        var key = ipAddress ?? "unknown";
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (_recentSubmissions.TryGetValue(key, out var lastTime)
                && (now - lastTime).TotalSeconds < 60)
            {
                return "Your request was received. Please wait before submitting another.";
            }
            _recentSubmissions[key] = now;

            // Prune entries older than 60 seconds to prevent unbounded growth
            var cutoff = now.AddSeconds(-60);
            foreach (var k in _recentSubmissions.Keys.Where(k => _recentSubmissions[k] < cutoff).ToList())
                _recentSubmissions.Remove(k);
        }

        await email.SendPrivacyRequestAsync(request);
        return null;
    }
}
```

- [ ] **Step 4: Register `PrivacyContactService` in `Program.cs`**

In `ChildDev.Api/Program.cs`, after the line `builder.Services.AddSingleton<EmailService>();`, add:

```csharp
builder.Services.AddSingleton<PrivacyContactService>();
```

- [ ] **Step 5: Run tests to confirm they pass**

```bash
dotnet test ChildDev.Api.Tests/ChildDev.Api.Tests.csproj --filter "PrivacyContactServiceTests" -q 2>&1 | tail -5
```

Expected: `Passed! — Failed: 0, Passed: 5`

- [ ] **Step 6: Commit**

```bash
git add ChildDev.Api/Services/PrivacyContactService.cs \
        ChildDev.Api/Program.cs \
        ChildDev.Api.Tests/PrivacyContactServiceTests.cs
git commit -m "feat: add PrivacyContactService with honeypot, timing, and rate-limit spam checks"
```

---

## Task 3: Privacy Policy Page (`/privacy`)

**Files:**
- Create: `ChildDev.Api/Components/Pages/Privacy.razor`

- [ ] **Step 1: Create the Privacy Policy page**

```razor
@page "/privacy"
@inject WebAnalyticsService Analytics
@inject IHttpContextAccessor HttpContextAccessor

<PageTitle>Privacy Policy – LevelUp</PageTitle>

<MudContainer MaxWidth="MaxWidth.Medium" Class="py-6">
    <MudLink Href="/" Underline="Underline.Hover" Color="Color.Secondary" Typo="Typo.body2" Class="mb-6 d-block">
        <MudIcon Icon="@Icons.Material.Filled.ArrowBack" Size="Size.Small" Style="vertical-align:middle;margin-right:4px;" />
        Back to Home
    </MudLink>

    <MudText Typo="Typo.h4" Style="font-weight:800;" Class="mb-1">Privacy Policy</MudText>
    <MudText Typo="Typo.body2" Color="Color.Secondary" Class="mb-6">
        Acknowledged Development Inc. · Effective May 28, 2026
    </MudText>

    <MudDivider Class="mb-6" />

    <!-- 1. Who We Are -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">1. Who We Are</MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;">
            LevelUp is operated by <strong>Acknowledged Development Inc.</strong>
            If you have any privacy questions or requests, contact us at
            <MudLink Href="mailto:privacy@securitasmachina.org" Color="Color.Primary">privacy@securitasmachina.org</MudLink>
            or via our <MudLink Href="/privacy/contact" Color="Color.Primary">Privacy Contact Form</MudLink>.
        </MudText>
    </section>

    <!-- 2. Who This App Is For -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">2. Who This App Is For</MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;">
            LevelUp is designed for children ages 5–18 and the adults who support them.
            <strong>Account holders must be 13 years of age or older.</strong>
            Parents and caregivers create and manage accounts on behalf of children under 13.
            Children use LevelUp under an adult account holder's authority. By creating an account,
            you confirm that you are 13 or older, or that you are a parent or guardian creating the
            account for a child in your care.
        </MudText>
    </section>

    <!-- 3. What We Collect -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">3. What We Collect</MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;" Class="mb-3">
            <strong>Information you provide:</strong>
        </MudText>
        <MudList T="string" Dense="true" Class="mb-3">
            <MudListItem Icon="@Icons.Material.Filled.Person">Nickname (a display name — not required to be a real name)</MudListItem>
            <MudListItem Icon="@Icons.Material.Filled.Lock">PIN / password (stored as a one-way BCrypt hash — never readable by us)</MudListItem>
            <MudListItem Icon="@Icons.Material.Filled.Email">Email address (optional — only used to notify you of goal completions)</MudListItem>
            <MudListItem Icon="@Icons.Material.Filled.EmojiEvents">Goals, progress notes, journal entries, to-do tasks, and reminders you create</MudListItem>
        </MudList>
        <MudText Typo="Typo.body1" Style="line-height:1.8;" Class="mb-3">
            <strong>Information generated automatically:</strong>
        </MudText>
        <MudList T="string" Dense="true">
            <MudListItem Icon="@Icons.Material.Filled.Analytics">
                Anonymous usage events (for example: "goal_create", "journal_view") tied to your account ID.
                These events do not include device identifiers, IP addresses, or advertising profiles.
            </MudListItem>
        </MudList>
    </section>

    <!-- 4. How We Use It -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">4. How We Use Your Information</MudText>
        <MudList T="string" Dense="true">
            <MudListItem Icon="@Icons.Material.Filled.Sync">To operate LevelUp and sync your data across your devices</MudListItem>
            <MudListItem Icon="@Icons.Material.Filled.Insights">To improve app features based on aggregate, anonymized usage patterns</MudListItem>
            <MudListItem Icon="@Icons.Material.Filled.Email">To send goal-completion notifications if you provided an email address</MudListItem>
        </MudList>
        <MudAlert Severity="Severity.Success" Class="mt-3" Dense="true">
            We do <strong>not</strong> sell your data. We do <strong>not</strong> show advertising.
            We do <strong>not</strong> share your data with third parties.
        </MudAlert>
    </section>

    <!-- 5. Data Storage & Security -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">5. Data Storage &amp; Security</MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;">
            Your data is stored on our servers in the United States and on your device.
            Your PIN is hashed with BCrypt and is never transmitted in readable form.
            Account sync is authenticated using JWT tokens.
            We implement reasonable technical and organizational security measures.
            No system is perfectly secure; if you have a security concern, please contact us immediately.
        </MudText>
    </section>

    <!-- 6. Your Rights -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">6. Your Rights</MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;" Class="mb-3">All users have the right to:</MudText>
        <MudList T="string" Dense="true" Class="mb-4">
            <MudListItem Icon="@Icons.Material.Filled.Info">Know what data we collect (this policy)</MudListItem>
            <MudListItem Icon="@Icons.Material.Filled.Download">Access or export your data (contact us)</MudListItem>
            <MudListItem Icon="@Icons.Material.Filled.DeleteForever">Delete your account and all data — via <MudLink Href="/settings" Color="Color.Primary">Settings → Delete My Account</MudLink> or our <MudLink Href="/privacy/contact" Color="Color.Primary">Privacy Contact Form</MudLink></MudListItem>
            <MudListItem Icon="@Icons.Material.Filled.Edit">Correct inaccurate data (contact us)</MudListItem>
        </MudList>
        <MudText Typo="Typo.body1" Style="line-height:1.8;">
            <strong>California residents</strong> have additional rights under the California Consumer Privacy Act (CCPA/CPRA),
            including the right to opt out of the sale of personal information (we do not sell data) and the right to
            limit the use of sensitive personal information (we use it only to operate the service).
        </MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;" Class="mt-3">
            <strong>Parents and guardians</strong> may request review or deletion of a child's account data at any time
            via our <MudLink Href="/privacy/contact" Color="Color.Primary">Privacy Contact Form</MudLink>.
        </MudText>
    </section>

    <!-- 7. Data Retention -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">7. Data Retention</MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;">
            Your data is kept until you delete your account. Deleted accounts and all associated data
            are purged from our servers within 30 days of deletion.
        </MudText>
    </section>

    <!-- 8. Children's Privacy (COPPA) -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">8. Children's Privacy</MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;">
            We take children's privacy seriously. We do not knowingly collect personal information from
            children under 13 without a parent or guardian acting as the account holder.
            If you believe that personal information has been collected from a child under 13 without
            appropriate parental authority, please contact us immediately at
            <MudLink Href="mailto:privacy@securitasmachina.org" Color="Color.Primary">privacy@securitasmachina.org</MudLink>
            or via our <MudLink Href="/privacy/contact" Color="Color.Primary">Privacy Contact Form</MudLink>,
            and we will delete the data promptly.
        </MudText>
    </section>

    <!-- 9. Changes -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">9. Changes to This Policy</MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;">
            We will update the effective date at the top of this page for any changes.
            Material changes will be noted in this section. Continued use of LevelUp after changes
            are posted constitutes your acceptance of the updated policy.
        </MudText>
    </section>

    <!-- 10. Contact -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">10. Contact Us</MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;">
            For privacy questions, data access requests, or to exercise your rights:
        </MudText>
        <MudStack Row="true" Spacing="2" Class="mt-3" Wrap="Wrap.Wrap">
            <MudButton Variant="Variant.Outlined" Color="Color.Primary" Href="/privacy/contact"
                       StartIcon="@Icons.Material.Filled.ContactMail">
                Privacy Contact Form
            </MudButton>
            <MudButton Variant="Variant.Text" Color="Color.Primary"
                       Href="mailto:privacy@securitasmachina.org"
                       StartIcon="@Icons.Material.Filled.Email">
                privacy@securitasmachina.org
            </MudButton>
        </MudStack>
    </section>

    <MudDivider Class="mb-4" />
    <MudStack Row="true" Spacing="3" Wrap="Wrap.Wrap">
        <MudLink Href="/terms" Color="Color.Secondary" Typo="Typo.body2">Terms of Service</MudLink>
        <MudText Typo="Typo.body2" Color="Color.Secondary">·</MudText>
        <MudLink Href="/privacy/contact" Color="Color.Secondary" Typo="Typo.body2">Privacy Contact Form</MudLink>
        <MudText Typo="Typo.body2" Color="Color.Secondary">·</MudText>
        <MudLink Href="/" Color="Color.Secondary" Typo="Typo.body2">Back to LevelUp</MudLink>
    </MudStack>
</MudContainer>

@code {
    protected override async Task OnInitializedAsync()
    {
        var accountGuid = HttpContextAccessor.HttpContext?.Session.GetString("AccountGuid");
        await Analytics.TrackAsync("page_view", accountGuid, "privacy");
    }
}
```

- [ ] **Step 2: Build and verify no compile errors**

```bash
dotnet build ChildDev.Api/ChildDev.Api.csproj -q 2>&1 | grep -E "error|Error" | grep -v "0 Error" | head -10
```

Expected: no output (clean build).

- [ ] **Step 3: Commit**

```bash
git add ChildDev.Api/Components/Pages/Privacy.razor
git commit -m "feat: add Privacy Policy page at /privacy (COPPA/CCPA compliant)"
```

---

## Task 4: Terms of Service Page (`/terms`)

**Files:**
- Create: `ChildDev.Api/Components/Pages/Terms.razor`

- [ ] **Step 1: Create the Terms of Service page**

```razor
@page "/terms"
@inject WebAnalyticsService Analytics
@inject IHttpContextAccessor HttpContextAccessor

<PageTitle>Terms of Service – LevelUp</PageTitle>

<MudContainer MaxWidth="MaxWidth.Medium" Class="py-6">
    <MudLink Href="/" Underline="Underline.Hover" Color="Color.Secondary" Typo="Typo.body2" Class="mb-6 d-block">
        <MudIcon Icon="@Icons.Material.Filled.ArrowBack" Size="Size.Small" Style="vertical-align:middle;margin-right:4px;" />
        Back to Home
    </MudLink>

    <MudText Typo="Typo.h4" Style="font-weight:800;" Class="mb-1">Terms of Service</MudText>
    <MudText Typo="Typo.body2" Color="Color.Secondary" Class="mb-6">
        Acknowledged Development Inc. · Effective May 28, 2026
    </MudText>

    <MudDivider Class="mb-6" />

    <!-- 1. Acceptance -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">1. Acceptance of Terms</MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;">
            By using LevelUp you agree to these Terms of Service. If you are creating an account on behalf of a child,
            you agree to these terms on their behalf and take responsibility for ensuring they are used appropriately.
        </MudText>
    </section>

    <!-- 2. Account Requirements -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">2. Account Requirements</MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;">
            Account holders must be 13 years of age or older. You are responsible for maintaining the
            confidentiality of your PIN and for all activity that occurs under your account.
            Notify us immediately at <MudLink Href="mailto:privacy@securitasmachina.org" Color="Color.Primary">privacy@securitasmachina.org</MudLink>
            if you believe your account has been compromised.
        </MudText>
    </section>

    <!-- 3. What LevelUp Is -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">3. What LevelUp Is</MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;">
            LevelUp is a free personal goal-tracking tool provided as-is. We make no guarantees of
            uptime, feature availability, or data persistence. LevelUp is <strong>not</strong> a substitute
            for professional therapeutic, educational, medical, or psychological services.
            LevelUp is not affiliated with any IEP (Individualized Education Program) process
            or official educational institution or program.
        </MudText>
    </section>

    <!-- 4. Your Content -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">4. Your Content</MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;">
            You own your goals, journal entries, progress notes, and tasks.
            By using LevelUp, you grant Acknowledged Development Inc. a limited, non-exclusive license
            to store, sync, and transmit your content solely as necessary to operate the service.
            We do not use your content for any other purpose.
        </MudText>
    </section>

    <!-- 5. Acceptable Use -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">5. Acceptable Use</MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;" Class="mb-2">You agree not to:</MudText>
        <MudList T="string" Dense="true">
            <MudListItem Icon="@Icons.Material.Filled.Block">Store or transmit illegal, harmful, or abusive content</MudListItem>
            <MudListItem Icon="@Icons.Material.Filled.Block">Attempt to reverse-engineer, scrape, or otherwise abuse the service</MudListItem>
            <MudListItem Icon="@Icons.Material.Filled.Block">Create accounts to impersonate other individuals</MudListItem>
            <MudListItem Icon="@Icons.Material.Filled.Block">Attempt to gain unauthorized access to other accounts or systems</MudListItem>
        </MudList>
    </section>

    <!-- 6. Account Deletion -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">6. Account Deletion</MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;">
            You may delete your account and all associated data at any time via
            <MudLink Href="/settings" Color="Color.Primary">Settings → Delete My Account</MudLink>.
            Server data is permanently purged within 30 days of deletion.
        </MudText>
    </section>

    <!-- 7. Disclaimer & Limitation of Liability -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">7. Disclaimer &amp; Limitation of Liability</MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;">
            LevelUp is provided "as is" without warranty of any kind, express or implied.
            Acknowledged Development Inc. is not liable for any data loss, service interruptions,
            or damages arising from your use of LevelUp. Because LevelUp is a free service,
            our total liability to you for any claim is limited to zero dollars ($0.00).
        </MudText>
    </section>

    <!-- 8. Changes -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">8. Changes to These Terms</MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;">
            We may update these terms from time to time. Material changes will be noted with an updated
            effective date. Continued use of LevelUp after changes are posted constitutes your acceptance
            of the updated terms.
        </MudText>
    </section>

    <!-- 9. Governing Law -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">9. Governing Law</MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;">
            These terms are governed by and construed in accordance with the laws of the State of California,
            United States, without regard to conflict of law principles.
        </MudText>
    </section>

    <!-- 10. Contact -->
    <section class="mb-6">
        <MudText Typo="Typo.h6" Style="font-weight:700;" Class="mb-2">10. Contact</MudText>
        <MudText Typo="Typo.body1" Style="line-height:1.8;">
            Questions about these terms? Contact us:
        </MudText>
        <MudStack Row="true" Spacing="2" Class="mt-3" Wrap="Wrap.Wrap">
            <MudButton Variant="Variant.Outlined" Color="Color.Primary" Href="/privacy/contact"
                       StartIcon="@Icons.Material.Filled.ContactMail">
                Contact Form
            </MudButton>
            <MudButton Variant="Variant.Text" Color="Color.Primary"
                       Href="mailto:privacy@securitasmachina.org"
                       StartIcon="@Icons.Material.Filled.Email">
                privacy@securitasmachina.org
            </MudButton>
        </MudStack>
    </section>

    <MudDivider Class="mb-4" />
    <MudStack Row="true" Spacing="3" Wrap="Wrap.Wrap">
        <MudLink Href="/privacy" Color="Color.Secondary" Typo="Typo.body2">Privacy Policy</MudLink>
        <MudText Typo="Typo.body2" Color="Color.Secondary">·</MudText>
        <MudLink Href="/privacy/contact" Color="Color.Secondary" Typo="Typo.body2">Privacy Contact Form</MudLink>
        <MudText Typo="Typo.body2" Color="Color.Secondary">·</MudText>
        <MudLink Href="/" Color="Color.Secondary" Typo="Typo.body2">Back to LevelUp</MudLink>
    </MudStack>
</MudContainer>

@code {
    protected override async Task OnInitializedAsync()
    {
        var accountGuid = HttpContextAccessor.HttpContext?.Session.GetString("AccountGuid");
        await Analytics.TrackAsync("page_view", accountGuid, "terms");
    }
}
```

- [ ] **Step 2: Build and verify no compile errors**

```bash
dotnet build ChildDev.Api/ChildDev.Api.csproj -q 2>&1 | grep -E "error|Error" | grep -v "0 Error" | head -10
```

Expected: no output.

- [ ] **Step 3: Commit**

```bash
git add ChildDev.Api/Components/Pages/Terms.razor
git commit -m "feat: add Terms of Service page at /terms"
```

---

## Task 5: Privacy Contact Form (`/privacy/contact`)

**Files:**
- Create: `ChildDev.Api/Components/Pages/PrivacyContact.razor`

- [ ] **Step 1: Create the Privacy Contact Form page**

```razor
@page "/privacy/contact"
@inject WebAnalyticsService Analytics
@inject IHttpContextAccessor HttpContextAccessor
@inject PrivacyContactService PrivacyContact

<PageTitle>Privacy Contact – LevelUp</PageTitle>

<MudContainer MaxWidth="MaxWidth.Small" Class="py-6">
    <MudLink Href="/privacy" Underline="Underline.Hover" Color="Color.Secondary" Typo="Typo.body2" Class="mb-6 d-block">
        <MudIcon Icon="@Icons.Material.Filled.ArrowBack" Size="Size.Small" Style="vertical-align:middle;margin-right:4px;" />
        Back to Privacy Policy
    </MudLink>

    <MudText Typo="Typo.h5" Style="font-weight:800;" Class="mb-1">Privacy Request</MudText>
    <MudText Typo="Typo.body2" Color="Color.Secondary" Class="mb-6">
        Data deletion requests, access requests, parental inquiries, and privacy questions.
        We respond within 45 days as required by law.
    </MudText>

    @if (_submitted)
    {
        <MudAlert Severity="Severity.Success" Class="mb-4">
            <MudText Typo="Typo.body1" Style="font-weight:600;">Request received.</MudText>
            <MudText Typo="Typo.body2" Class="mt-1">
                We received your request and will respond within 45 days as required by law.
            </MudText>
            @if (_requestType == "Delete my account and all data")
            {
                <MudText Typo="Typo.body2" Class="mt-2">
                    For immediate self-service deletion:
                    <MudLink Href="/settings" Color="Color.Primary">Settings → Delete My Account</MudLink>
                </MudText>
            }
        </MudAlert>
    }
    else
    {
        @if (_errorMessage is not null)
        {
            <MudAlert Severity="Severity.Warning" Class="mb-4">@_errorMessage</MudAlert>
        }

        <MudCard Elevation="2">
            <MudCardContent Class="pa-5">
                <MudTextField @bind-Value="_name" Label="Your Name" Variant="Variant.Outlined"
                              Required="true" Class="mb-4" />

                <MudTextField @bind-Value="_email" Label="Email Address" Variant="Variant.Outlined"
                              Required="true" InputType="InputType.Email" Class="mb-4"
                              HelperText="We will respond to this address" />

                <MudSelect @bind-Value="_requestType" Label="Request Type" Variant="Variant.Outlined"
                           Required="true" Class="mb-4">
                    <MudSelectItem Value="@("Delete my account and all data")">Delete my account and all data</MudSelectItem>
                    <MudSelectItem Value="@("Access / export my data")">Access / export my data</MudSelectItem>
                    <MudSelectItem Value="@("Question about privacy practices")">Question about privacy practices</MudSelectItem>
                    <MudSelectItem Value="@("Parental request regarding a child's account")">Parental request regarding a child's account</MudSelectItem>
                    <MudSelectItem Value="@("Other")">Other</MudSelectItem>
                </MudSelect>

                <MudTextField @bind-Value="_nickname" Label="Account Nickname (optional)" Variant="Variant.Outlined"
                              Class="mb-4" HelperText="Helps us locate your account" />

                <MudTextField @bind-Value="_message" Label="Message" Variant="Variant.Outlined"
                              Lines="5" MaxLength="2000" Required="true" Class="mb-4"
                              Counter="2000" Immediate="true" />

                <MudCheckBox @bind-Value="_confirmed" Color="Color.Primary" Class="mb-4">
                    <MudText Typo="Typo.body2">
                        I confirm I am the account holder or parent/guardian of the account holder
                    </MudText>
                </MudCheckBox>

                {{!-- Honeypot: hidden from real users, bots fill it --}}
                <div style="display:none;" aria-hidden="true">
                    <input type="text" @bind="_website" tabindex="-1" autocomplete="off" name="website" />
                </div>
            </MudCardContent>
            <MudCardActions Class="px-5 pb-5">
                <MudButton Variant="Variant.Filled" Color="Color.Primary" FullWidth="true"
                           OnClick="HandleSubmit"
                           Disabled="@(!CanSubmit)"
                           StartIcon="@Icons.Material.Filled.Send">
                    @(_loading ? "Sending…" : "Submit Request")
                </MudButton>
            </MudCardActions>
        </MudCard>

        <MudText Typo="Typo.caption" Color="Color.Secondary" Class="mt-4 d-block text-center">
            You can also email us directly at
            <MudLink Href="mailto:privacy@securitasmachina.org" Color="Color.Secondary" Typo="Typo.caption">
                privacy@securitasmachina.org
            </MudLink>
        </MudText>
    }
</MudContainer>

@code {
    private DateTime _renderTime;
    private string _name = string.Empty;
    private string _email = string.Empty;
    private string _requestType = string.Empty;
    private string _nickname = string.Empty;
    private string _message = string.Empty;
    private string _website = string.Empty; // honeypot
    private bool _confirmed;
    private bool _submitted;
    private bool _loading;
    private string? _errorMessage;

    private bool CanSubmit =>
        !string.IsNullOrWhiteSpace(_name)
        && !string.IsNullOrWhiteSpace(_email)
        && !string.IsNullOrWhiteSpace(_requestType)
        && !string.IsNullOrWhiteSpace(_message)
        && _confirmed
        && !_loading;

    protected override async Task OnInitializedAsync()
    {
        _renderTime = DateTime.UtcNow;
        var accountGuid = HttpContextAccessor.HttpContext?.Session.GetString("AccountGuid");
        await Analytics.TrackAsync("page_view", accountGuid, "privacy_contact");
    }

    private async Task HandleSubmit()
    {
        _loading = true;
        _errorMessage = null;

        var elapsed = (DateTime.UtcNow - _renderTime).TotalSeconds;
        var ip = HttpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

        var request = new PrivacyContactRequest(
            Name: _name.Trim(),
            Email: _email.Trim(),
            RequestType: _requestType,
            Nickname: _nickname.Trim(),
            Message: _message.Trim(),
            Website: _website,
            ElapsedSeconds: elapsed);

        var error = await PrivacyContact.SubmitAsync(request, ip);

        if (error is null)
        {
            _submitted = true;
            var accountGuid = HttpContextAccessor.HttpContext?.Session.GetString("AccountGuid");
            await Analytics.TrackAsync("privacy_contact_submit", accountGuid, "privacy_contact", _requestType);
        }
        else
        {
            _errorMessage = error;
        }

        _loading = false;
    }
}
```

- [ ] **Step 2: Build and verify no compile errors**

```bash
dotnet build ChildDev.Api/ChildDev.Api.csproj -q 2>&1 | grep -E "error|Error" | grep -v "0 Error" | head -10
```

Expected: no output.

- [ ] **Step 3: Commit**

```bash
git add ChildDev.Api/Components/Pages/PrivacyContact.razor
git commit -m "feat: add Privacy Contact Form at /privacy/contact with honeypot + timing + rate-limit"
```

---

## Task 6: Register Page — Consent Checkboxes

**Files:**
- Modify: `ChildDev.Api/Components/Pages/Register.razor`

- [ ] **Step 1: Add consent checkbox state fields to the `@code` block**

In `Register.razor`, in the `@code` block, add two new fields after `private string Email = string.Empty;`:

```csharp
    private bool _ageConsent;
    private bool _policyConsent;
```

- [ ] **Step 2: Update the `Disabled` condition on the Create Account button**

Find the existing button:

```razor
Disabled="@(string.IsNullOrWhiteSpace(NickName) || Pin.Length < 4 || Pin != ConfirmPin)"
```

Replace it with:

```razor
Disabled="@(string.IsNullOrWhiteSpace(NickName) || Pin.Length < 4 || Pin != ConfirmPin || !_ageConsent || !_policyConsent)"
```

- [ ] **Step 3: Add the two consent checkboxes above the Create Account button**

Find the `<MudCardActions>` section. Before the `<MudButton ... Create Account ...>` line, insert:

```razor
                <MudCheckBox @bind-Value="_ageConsent" Color="Color.Primary" Dense="true" Class="mb-2 align-start">
                    <MudText Typo="Typo.body2">
                        I am 13 or older, or I am a parent or guardian creating this account on behalf of a child
                    </MudText>
                </MudCheckBox>
                <MudCheckBox @bind-Value="_policyConsent" Color="Color.Primary" Dense="true" Class="mb-3 align-start">
                    <MudText Typo="Typo.body2">
                        I have read and agree to the
                        <MudLink Href="/privacy" Target="_blank" Color="Color.Primary">Privacy Policy</MudLink>
                        and
                        <MudLink Href="/terms" Target="_blank" Color="Color.Primary">Terms of Service</MudLink>
                    </MudText>
                </MudCheckBox>
```

- [ ] **Step 4: Build and verify no compile errors**

```bash
dotnet build ChildDev.Api/ChildDev.Api.csproj -q 2>&1 | grep -E "error|Error" | grep -v "0 Error" | head -10
```

Expected: no output.

- [ ] **Step 5: Commit**

```bash
git add ChildDev.Api/Components/Pages/Register.razor
git commit -m "feat: add age consent and policy agreement checkboxes to Register page"
```

---

## Task 7: Navigation Integration

**Files:**
- Modify: `ChildDev.Api/Components/Layout/MainLayout.razor`
- Modify: `ChildDev.Api/Components/Pages/About.razor`
- Modify: `ChildDev.Api/Components/Pages/Settings.razor`

- [ ] **Step 1: Add Privacy and Terms links to MainLayout footer**

In `MainLayout.razor`, find the existing footer section (the `MudPaper` with the Android download link). After the existing `<MudText Typo="Typo.caption" Color="Color.Secondary">·</MudText>` and About link block, add:

```razor
                    <MudText Typo="Typo.caption" Color="Color.Secondary">·</MudText>
                    <MudLink Href="/privacy" Typo="Typo.caption" Color="Color.Secondary" Style="opacity:.8;">Privacy Policy</MudLink>
                    <MudText Typo="Typo.caption" Color="Color.Secondary">·</MudText>
                    <MudLink Href="/terms" Typo="Typo.caption" Color="Color.Secondary" Style="opacity:.8;">Terms</MudLink>
```

Add these immediately after the existing `About LevelUp` link block:

```razor
                    <MudText Typo="Typo.caption" Color="Color.Secondary">·</MudText>
                    <MudLink Href="/about" Typo="Typo.caption" Color="Color.Secondary" Style="opacity:.8;">About LevelUp</MudLink>
                    <MudText Typo="Typo.caption" Color="Color.Secondary">·</MudText>
                    <MudLink Href="/privacy" Typo="Typo.caption" Color="Color.Secondary" Style="opacity:.8;">Privacy Policy</MudLink>
                    <MudText Typo="Typo.caption" Color="Color.Secondary">·</MudText>
                    <MudLink Href="/terms" Typo="Typo.caption" Color="Color.Secondary" Style="opacity:.8;">Terms</MudLink>
```

The full replacement block (the `MudStack` inside the `MudPaper` footer) becomes:

```razor
                <MudStack Row="true" AlignItems="AlignItems.Center" Justify="Justify.Center" Spacing="2" Wrap="Wrap.Wrap">
                    <MudIcon Icon="@Icons.Material.Filled.Android" Size="Size.Small" Style="color:#3DDC84;" />
                    <MudLink Href="https://downloads.securitasmachina.org/" Target="_blank" Typo="Typo.caption" Color="Color.Primary"
                             Style="font-weight:600;">Download LevelUp for Android</MudLink>
                    <MudText Typo="Typo.caption" Color="Color.Secondary">— install the mobile app</MudText>
                    @if (NickName is not null)
                    {
                        <MudText Typo="Typo.caption" Color="Color.Secondary">·</MudText>
                        <MudLink Typo="Typo.caption" Color="Color.Secondary" Style="cursor:pointer;"
                                 OnClick="OpenHelp">⌨️ Keyboard shortcuts</MudLink>
                    }
                    <MudText Typo="Typo.caption" Color="Color.Secondary">·</MudText>
                    <MudLink Href="/about" Typo="Typo.caption" Color="Color.Secondary" Style="opacity:.8;">About LevelUp</MudLink>
                    <MudText Typo="Typo.caption" Color="Color.Secondary">·</MudText>
                    <MudLink Href="/privacy" Typo="Typo.caption" Color="Color.Secondary" Style="opacity:.8;">Privacy Policy</MudLink>
                    <MudText Typo="Typo.caption" Color="Color.Secondary">·</MudText>
                    <MudLink Href="/terms" Typo="Typo.caption" Color="Color.Secondary" Style="opacity:.8;">Terms</MudLink>
                    <MudText Typo="Typo.caption" Color="Color.Secondary">·</MudText>
                    <MudText Typo="Typo.caption" Color="Color.Secondary" Style="opacity:.6;">Build: @ChildDev.Api.BuildInfo.BuildTimestamp</MudText>
                </MudStack>
```

- [ ] **Step 2: Add legal footer to About page**

In `About.razor`, find the existing footer `<MudLink Href="/" ...>Back to Home</MudLink>` at the very bottom (before `</MudContainer>`). Replace it with:

```razor
    <MudDivider Class="mb-4" />
    <MudStack Row="true" Spacing="3" Wrap="Wrap.Wrap" AlignItems="AlignItems.Center">
        <MudLink Href="/privacy" Color="Color.Secondary" Typo="Typo.body2">Privacy Policy</MudLink>
        <MudText Typo="Typo.body2" Color="Color.Secondary">·</MudText>
        <MudLink Href="/terms" Color="Color.Secondary" Typo="Typo.body2">Terms of Service</MudLink>
        <MudText Typo="Typo.body2" Color="Color.Secondary">·</MudText>
        <MudLink Href="/privacy/contact" Color="Color.Secondary" Typo="Typo.body2">Privacy Contact</MudLink>
        <MudText Typo="Typo.body2" Color="Color.Secondary">·</MudText>
        <MudLink Href="/" Underline="Underline.Hover" Color="Color.Secondary" Typo="Typo.body2">
            <MudIcon Icon="@Icons.Material.Filled.ArrowBack" Size="Size.Small" Style="vertical-align:middle;margin-right:4px;" />
            Back to Home
        </MudLink>
    </MudStack>
```

- [ ] **Step 3: Add privacy contact link to Settings page**

In `Settings.razor`, find this exact block (around line 230):

```razor
    }
</MudPaper>
```

Replace it with:

```razor
    }
    <MudText Typo="Typo.body2" Color="Color.Secondary" Class="mt-3">
        You can also submit a privacy request or ask questions at
        <MudLink Href="/privacy/contact" Color="Color.Primary">Privacy Contact →</MudLink>
    </MudText>
</MudPaper>
```

- [ ] **Step 4: Build and verify no compile errors**

```bash
dotnet build ChildDev.Api/ChildDev.Api.csproj -q 2>&1 | grep -E "error|Error" | grep -v "0 Error" | head -10
```

Expected: no output.

- [ ] **Step 5: Run all API tests to confirm nothing broke**

```bash
dotnet test ChildDev.Api.Tests/ChildDev.Api.Tests.csproj -q 2>&1 | tail -5
```

Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add ChildDev.Api/Components/Layout/MainLayout.razor \
        ChildDev.Api/Components/Pages/About.razor \
        ChildDev.Api/Components/Pages/Settings.razor
git commit -m "feat: add Privacy Policy and Terms links to footer, About, and Settings pages"
```

---

## Task 8: Final Verification

- [ ] **Step 1: Run the full API test suite**

```bash
dotnet test ChildDev.Api.Tests/ChildDev.Api.Tests.csproj -q 2>&1 | tail -5
```

Expected: all tests pass, no failures.

- [ ] **Step 2: Verify all new routes exist in the build**

```bash
grep -r "@page \"/privacy" ChildDev.Api/Components/Pages/ --include="*.razor"
```

Expected output (3 lines):
```
ChildDev.Api/Components/Pages/Privacy.razor:@page "/privacy"
ChildDev.Api/Components/Pages/Terms.razor:@page "/terms"
ChildDev.Api/Components/Pages/PrivacyContact.razor:@page "/privacy/contact"
```

- [ ] **Step 3: Verify `PrivacyContactService` is registered**

```bash
grep "PrivacyContactService" ChildDev.Api/Program.cs
```

Expected: `builder.Services.AddSingleton<PrivacyContactService>();`

- [ ] **Step 4: Verify Register page has both consent fields**

```bash
grep "_ageConsent\|_policyConsent" ChildDev.Api/Components/Pages/Register.razor | wc -l
```

Expected: `4` or more (field declarations + checkbox bindings + button disabled condition).

- [ ] **Step 5: Final commit and push**

```bash
git push origin master
```

---

## Google Play Store Setup Reference

When submitting to Google Play, use these values:

| Field | Value |
|---|---|
| Privacy Policy URL | `https://levelup.securitasmachina.org/privacy` |
| App category | Education |
| Target audience | Children and adults (Mixed audience) |
| Contains ads? | No |
| Data collected | App activity, App info and performance |
| Data shared with third parties? | No |
| Data encrypted in transit? | Yes |
| Users can request deletion? | Yes |
| Developer email | `privacy@securitasmachina.org` |
