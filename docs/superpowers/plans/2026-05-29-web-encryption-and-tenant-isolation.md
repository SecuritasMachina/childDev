# Web Encryption at Rest & Tenant Isolation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Encrypt sensitive free-text columns at rest in MariaDB and enforce per-account data isolation at the `AppDbContext` level so web clients can never see each other's data.

**Architecture:** An AES-256-GCM `ValueConverter` encrypts unbounded `TEXT` columns with a version-tagged format (`v1:…`) that treats legacy plaintext as pass-through for zero-downtime migration. EF Core global query filters scope all tenant entities to a current-account value resolved JWT-first (mobile-sync API) then session (Blazor). A wrapping `IDbContextFactory` guarantees every Blazor-created context is scoped.

**Tech Stack:** ASP.NET Core 8, EF Core (Pomelo MySQL), Blazor Server, xUnit (`ChildDev.Api.Tests`), AES-GCM via `System.Security.Cryptography`.

**Spec:** `docs/superpowers/specs/2026-05-29-web-encryption-and-tenant-isolation-design.md`

---

## File Structure

- Create `ChildDev.Api/Services/EncryptionService.cs` — holds the AES key, exposes `Encrypt`/`Decrypt`.
- Create `ChildDev.Api/Data/EncryptedStringConverter.cs` — EF `ValueConverter<string,string>` delegating to `EncryptionService`.
- Create `ChildDev.Api/Services/ICurrentAccountProvider.cs` + `CurrentAccountProvider.cs` — resolves account (JWT → session).
- Create `ChildDev.Api/Data/ScopedDbContextFactory.cs` — wraps the real factory, sets `AccountGuid`.
- Create `ChildDev.Api/Services/EncryptionMigrationHostedService.cs` — one-shot lazy re-encrypt pass.
- Modify `ChildDev.Api/Data/AppDbContext.cs` — add `AccountGuid` prop, query filters, converter mappings.
- Modify `ChildDev.Api/Program.cs` — read key, register services, swap factory registration.
- Modify `.env.example` — document `CHILDDEV_ENC_KEY`.
- Tests in `ChildDev.Api.Tests/`.

**Key wiring facts (verified):**
- `JwtService.ExtractAccountGuid(ClaimsPrincipal)` returns the `accountGuid` claim (Singleton).
- Both `AddDbContext<AppDbContext>` and `AddDbContextFactory<AppDbContext>` are registered (`Program.cs:15-21`).
- `IHttpContextAccessor` is already registered (`Program.cs:74`).
- Session key is `"AccountGuid"`. Tenant entities use `AccountFk` (Goal/Journal/GoalProgress/Todo) or `AccountGuid` (Reminder/AnalyticsEvent).
- Phase-1 encrypted columns (already `LONGTEXT`, no `ALTER` needed): `Goal.GoalText`, `Goal.MeasurableOutcome`, `Goal.Steps`, `Journal.Notes`, `GoalProgress.NextStepItems`, `Todo.Notes`.

---

## Task 1: EncryptionService (AES-GCM, version-tagged)

**Files:**
- Create: `ChildDev.Api/Services/EncryptionService.cs`
- Test: `ChildDev.Api.Tests/EncryptionServiceTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ChildDev.Api.Services;
using Xunit;

namespace ChildDev.Api.Tests;

public class EncryptionServiceTests
{
    // 32 raw bytes, base64-encoded
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
    private static EncryptionService Svc() => new(Key);

    [Fact]
    public void RoundTrips_PlaintextThroughCipher()
    {
        var svc = Svc();
        var ct = svc.Encrypt("hello world");
        Assert.StartsWith("v1:", ct);
        Assert.NotEqual("hello world", ct);
        Assert.Equal("hello world", svc.Decrypt(ct));
    }

    [Fact]
    public void Decrypt_LegacyPlaintext_PassesThrough()
    {
        var svc = Svc();
        Assert.Equal("legacy notes", svc.Decrypt("legacy notes"));
    }

    [Fact]
    public void NullAndEmpty_PassThrough()
    {
        var svc = Svc();
        Assert.Null(svc.Encrypt(null));
        Assert.Equal("", svc.Encrypt(""));
        Assert.Null(svc.Decrypt(null));
        Assert.Equal("", svc.Decrypt(""));
    }

    [Fact]
    public void Encrypt_UsesFreshNonce_DifferentCiphertextSamePlaintext()
    {
        var svc = Svc();
        Assert.NotEqual(svc.Encrypt("same"), svc.Encrypt("same"));
    }

    [Fact]
    public void Decrypt_Tampered_Throws()
    {
        var svc = Svc();
        var ct = svc.Encrypt("secret");
        var bad = ct[..^2] + (ct.EndsWith("A") ? "B" : "A");
        Assert.ThrowsAny<Exception>(() => svc.Decrypt(bad));
    }

    [Fact]
    public void Constructor_RejectsWrongKeyLength()
    {
        Assert.Throws<ArgumentException>(() => new EncryptionService(Convert.ToBase64String(new byte[16])));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ChildDev.Api.Tests/ChildDev.Api.Tests.csproj --filter EncryptionServiceTests`
Expected: FAIL — `EncryptionService` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace ChildDev.Api.Services;

/// <summary>AES-256-GCM string encryption with a version-tagged, backward-compatible format.</summary>
public sealed class EncryptionService
{
    private const string Prefix = "v1:";
    private const int NonceSize = 12; // AES-GCM standard nonce
    private const int TagSize = 16;   // AES-GCM tag
    private readonly byte[] _key;

    public EncryptionService(string base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
            throw new ArgumentException("Encryption key is not configured.", nameof(base64Key));
        byte[] key;
        try { key = Convert.FromBase64String(base64Key); }
        catch (FormatException) { throw new ArgumentException("Encryption key must be base64.", nameof(base64Key)); }
        if (key.Length != 32)
            throw new ArgumentException($"Encryption key must decode to 32 bytes, got {key.Length}.", nameof(base64Key));
        _key = key;
    }

    public string? Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ct = new byte[pt.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, pt, ct, tag);
        var blob = new byte[NonceSize + TagSize + ct.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, NonceSize, TagSize);
        Buffer.BlockCopy(ct, 0, blob, NonceSize + TagSize, ct.Length);
        return Prefix + Convert.ToBase64String(blob);
    }

    public string? Decrypt(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored; // legacy plaintext
        var blob = Convert.FromBase64String(stored.Substring(Prefix.Length));
        var nonce = blob.AsSpan(0, NonceSize);
        var tag = blob.AsSpan(NonceSize, TagSize);
        var ct = blob.AsSpan(NonceSize + TagSize);
        var pt = new byte[ct.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ct, tag, pt);
        return Encoding.UTF8.GetString(pt);
    }

    public bool IsEncrypted(string? stored) =>
        stored is not null && stored.StartsWith(Prefix, StringComparison.Ordinal);
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ChildDev.Api.Tests/ChildDev.Api.Tests.csproj --filter EncryptionServiceTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add ChildDev.Api/Services/EncryptionService.cs ChildDev.Api.Tests/EncryptionServiceTests.cs
git commit -m "feat(api): AES-GCM EncryptionService with version-tagged backward-compatible format"
```

---

## Task 2: EncryptedStringConverter

**Files:**
- Create: `ChildDev.Api/Data/EncryptedStringConverter.cs`
- Test: `ChildDev.Api.Tests/EncryptedStringConverterTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ChildDev.Api.Data;
using ChildDev.Api.Services;
using Xunit;

namespace ChildDev.Api.Tests;

public class EncryptedStringConverterTests
{
    private static EncryptionService Svc() =>
        new(Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()));

    [Fact]
    public void Converter_EncryptsToProvider_DecryptsFromProvider()
    {
        var svc = Svc();
        var conv = new EncryptedStringConverter(svc);
        var toProvider = conv.ConvertToProvider.Compile();
        var fromProvider = conv.ConvertFromProvider.Compile();

        var stored = (string?)toProvider("note")!;
        Assert.StartsWith("v1:", stored);
        Assert.Equal("note", (string?)fromProvider(stored));
        // legacy plaintext read path
        Assert.Equal("legacy", (string?)fromProvider("legacy"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ChildDev.Api.Tests/ChildDev.Api.Tests.csproj --filter EncryptedStringConverterTests`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Implement**

```csharp
using ChildDev.Api.Services;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ChildDev.Api.Data;

/// <summary>EF value converter that encrypts strings at rest via <see cref="EncryptionService"/>.</summary>
public sealed class EncryptedStringConverter : ValueConverter<string?, string?>
{
    public EncryptedStringConverter(EncryptionService enc)
        : base(v => enc.Encrypt(v), v => enc.Decrypt(v)) { }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ChildDev.Api.Tests/ChildDev.Api.Tests.csproj --filter EncryptedStringConverterTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ChildDev.Api/Data/EncryptedStringConverter.cs ChildDev.Api.Tests/EncryptedStringConverterTests.cs
git commit -m "feat(api): EF EncryptedStringConverter"
```

---

## Task 3: CurrentAccountProvider (JWT → session)

**Files:**
- Create: `ChildDev.Api/Services/ICurrentAccountProvider.cs`
- Create: `ChildDev.Api/Services/CurrentAccountProvider.cs`
- Test: `ChildDev.Api.Tests/CurrentAccountProviderTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ChildDev.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using Xunit;

namespace ChildDev.Api.Tests;

public class CurrentAccountProviderTests
{
    private static JwtService Jwt()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CHILDDEV_JWT_SECRET"] = "test-secret-test-secret-test-secret" })
            .Build();
        return new JwtService(cfg);
    }

    [Fact]
    public void Prefers_JwtClaim_OverSession()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("accountGuid", "JWT-ACC") }, "jwt"));
        var accessor = new HttpContextAccessor { HttpContext = ctx };
        var p = new CurrentAccountProvider(accessor, Jwt());
        Assert.Equal("JWT-ACC", p.GetAccountGuid());
    }

    [Fact]
    public void Returns_Null_WhenNoIdentity()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var p = new CurrentAccountProvider(accessor, Jwt());
        Assert.Null(p.GetAccountGuid());
    }
}
```

(Session fallback is covered by integration tests in Task 6, where a real session exists.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ChildDev.Api.Tests/ChildDev.Api.Tests.csproj --filter CurrentAccountProviderTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement**

`ICurrentAccountProvider.cs`:
```csharp
namespace ChildDev.Api.Services;

public interface ICurrentAccountProvider
{
    string? GetAccountGuid();
}
```

`CurrentAccountProvider.cs`:
```csharp
using Microsoft.AspNetCore.Http;

namespace ChildDev.Api.Services;

public sealed class CurrentAccountProvider(IHttpContextAccessor accessor, JwtService jwt) : ICurrentAccountProvider
{
    public string? GetAccountGuid()
    {
        var ctx = accessor.HttpContext;
        if (ctx is null) return null;

        // 1) JWT (mobile-sync / API)
        var fromJwt = jwt.ExtractAccountGuid(ctx.User);
        if (!string.IsNullOrEmpty(fromJwt)) return fromJwt;

        // 2) Session (Blazor web). Session may be unavailable outside request scope.
        try { return ctx.Session.GetString("AccountGuid"); }
        catch { return null; }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ChildDev.Api.Tests/ChildDev.Api.Tests.csproj --filter CurrentAccountProviderTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ChildDev.Api/Services/ICurrentAccountProvider.cs ChildDev.Api/Services/CurrentAccountProvider.cs ChildDev.Api.Tests/CurrentAccountProviderTests.cs
git commit -m "feat(api): CurrentAccountProvider resolving JWT-then-session account"
```

---

## Task 4: AppDbContext — AccountGuid, query filters, converter mappings

**Files:**
- Modify: `ChildDev.Api/Data/AppDbContext.cs`

- [ ] **Step 1: Replace the context with filtered + encrypted mappings**

Full new `AppDbContext.cs`:
```csharp
using ChildDev.Api.Data;
using ChildDev.Api.Models.Entities;
using ChildDev.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Data;

public class AppDbContext : DbContext
{
    private readonly EncryptionService? _enc;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public AppDbContext(DbContextOptions<AppDbContext> options, EncryptionService enc) : base(options) { _enc = enc; }

    /// <summary>Current tenant. When null, tenant entity queries return no rows.</summary>
    public string? AccountGuid { get; set; }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Journal> Journals => Set<Journal>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<GoalProgress> GoalProgresses => Set<GoalProgress>();
    public DbSet<Todo> Todos => Set<Todo>();
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();
    public DbSet<Reminder> Reminders => Set<Reminder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Journal>().HasIndex(j => new { j.AccountFk, j.UpdatedOn });
        modelBuilder.Entity<Goal>().HasIndex(g => new { g.AccountFk, g.UpdatedOn });
        modelBuilder.Entity<GoalProgress>().HasIndex(p => new { p.AccountFk, p.UpdatedOn });
        modelBuilder.Entity<GoalProgress>().HasIndex(p => new { p.AccountFk, p.GoalFk });
        modelBuilder.Entity<Todo>().HasIndex(t => new { t.AccountFk, t.UpdatedOn });
        modelBuilder.Entity<Account>().HasIndex(a => a.NickName).IsUnique();
        modelBuilder.Entity<AnalyticsEvent>().HasIndex(e => new { e.AccountGuid, e.Timestamp });
        modelBuilder.Entity<Reminder>().HasIndex(r => new { r.AccountGuid, r.IsDismissed });

        // Tenant isolation — global query filters. Account is intentionally NOT filtered
        // (login/register look up by NickName before any AccountGuid exists).
        modelBuilder.Entity<Goal>().HasQueryFilter(g => g.AccountFk == AccountGuid);
        modelBuilder.Entity<Journal>().HasQueryFilter(j => j.AccountFk == AccountGuid);
        modelBuilder.Entity<GoalProgress>().HasQueryFilter(p => p.AccountFk == AccountGuid);
        modelBuilder.Entity<Todo>().HasQueryFilter(t => t.AccountFk == AccountGuid);
        modelBuilder.Entity<Reminder>().HasQueryFilter(r => r.AccountGuid == AccountGuid);
        modelBuilder.Entity<AnalyticsEvent>().HasQueryFilter(e => e.AccountGuid == AccountGuid);

        // Encryption at rest — Phase 1: unbounded TEXT columns only (no ALTER needed).
        if (_enc is not null)
        {
            var conv = new EncryptedStringConverter(_enc);
            modelBuilder.Entity<Goal>().Property(g => g.GoalText).HasConversion(conv);
            modelBuilder.Entity<Goal>().Property(g => g.MeasurableOutcome).HasConversion(conv);
            modelBuilder.Entity<Goal>().Property(g => g.Steps).HasConversion(conv);
            modelBuilder.Entity<Journal>().Property(j => j.Notes).HasConversion(conv);
            modelBuilder.Entity<GoalProgress>().Property(p => p.NextStepItems).HasConversion(conv);
            modelBuilder.Entity<Todo>().Property(t => t.Notes).HasConversion(conv);
        }
    }
}
```

> NOTE: The query filter references the instance property `AccountGuid`; EF re-evaluates it per query, so each context instance is scoped to whatever `AccountGuid` was set. Setting it requires a fresh/scoped context (handled in Tasks 5 & 6).

- [ ] **Step 2: Build**

Run: `dotnet build ChildDev.Api/ChildDev.Api.csproj`
Expected: builds. (Tests for behavior run in Task 6 after wiring.)

- [ ] **Step 3: Commit**

```bash
git add ChildDev.Api/Data/AppDbContext.cs
git commit -m "feat(api): global query filters + encrypted column mappings on AppDbContext"
```

---

## Task 5: ScopedDbContextFactory + Program.cs wiring

**Files:**
- Create: `ChildDev.Api/Data/ScopedDbContextFactory.cs`
- Modify: `ChildDev.Api/Program.cs`

- [ ] **Step 1: Create the wrapping factory**

```csharp
using ChildDev.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Data;

/// <summary>
/// Wraps the EF-generated pooled/scoped factory so every Blazor-created context is
/// auto-scoped to the current account. Guarantees isolation even if a page forgets to filter.
/// </summary>
public sealed class ScopedDbContextFactory(
    IDbContextFactory<AppDbContext> inner,
    ICurrentAccountProvider accounts) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext()
    {
        var ctx = inner.CreateDbContext();
        ctx.AccountGuid = accounts.GetAccountGuid();
        return ctx;
    }

    public async Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
    {
        var ctx = await inner.CreateDbContextAsync(ct);
        ctx.AccountGuid = accounts.GetAccountGuid();
        return ctx;
    }
}
```

- [ ] **Step 2: Wire Program.cs — read key, register services, swap factory**

After the connection-string lines (`Program.cs:12-13`), add the key read:
```csharp
var encKey = builder.Configuration["CHILDDEV_ENC_KEY"]
    ?? throw new InvalidOperationException(
        "CHILDDEV_ENC_KEY is not configured (base64 32-byte AES key from ~/data/.secrets/levelUp.enckey).");
builder.Services.AddSingleton(new EncryptionService(encKey));
```

Replace the `AddDbContext`/`AddDbContextFactory` block (`Program.cs:15-21`) with one that injects `EncryptionService` into each context. Because the EF DI extensions don't pass extra ctor args, register the context via a factory lambda that resolves `EncryptionService`:

```csharp
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
        mySqlOptions => mySqlOptions.CommandTimeout(8)),
    ServiceLifetime.Scoped);
builder.Services.AddDbContextFactory<AppDbContext>((sp, options) =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
        mySqlOptions => mySqlOptions.CommandTimeout(8)),
    ServiceLifetime.Scoped);
```

> The `EncryptionService` ctor is selected automatically by EF only if registered; to be explicit and avoid ctor ambiguity, EF uses the `(DbContextOptions, EncryptionService)` constructor when `EncryptionService` is resolvable from the internal service provider. To guarantee this, register it via `AddDbContext`'s internal services: replace the two registrations above with the `replaceService`-free approach using a `IDbContextOptionsConfiguration` is overkill — instead use the simplest reliable mechanism:

Use an explicit options + manual scoped registration so the encryption ctor is always used:

```csharp
// Scoped AppDbContext (API/mobile-sync path) — encryption + account scoping
builder.Services.AddScoped(sp =>
{
    var opts = new DbContextOptionsBuilder<AppDbContext>()
        .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
            o => o.CommandTimeout(8))
        .Options;
    var ctx = new AppDbContext(opts, sp.GetRequiredService<EncryptionService>());
    ctx.AccountGuid = sp.GetRequiredService<ICurrentAccountProvider>().GetAccountGuid();
    return ctx;
});

// Inner pooled-style factory used by the scoped wrapper (Blazor path)
builder.Services.AddSingleton<Func<AppDbContext>>(sp =>
{
    var enc = sp.GetRequiredService<EncryptionService>();
    return () =>
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
                o => o.CommandTimeout(8))
            .Options;
        return new AppDbContext(opts, enc);
    };
});
```

Then register account provider and the public factory (replace the EF factory entirely):
```csharp
builder.Services.AddScoped<ICurrentAccountProvider, CurrentAccountProvider>();
builder.Services.AddScoped<IDbContextFactory<AppDbContext>, ScopedDbContextFactory>();
```

And update `ScopedDbContextFactory` to consume the `Func<AppDbContext>` instead of EF's `IDbContextFactory` (revised to avoid registering EF's factory at all):

```csharp
public sealed class ScopedDbContextFactory(
    Func<AppDbContext> create,
    ICurrentAccountProvider accounts) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext()
    {
        var ctx = create();
        ctx.AccountGuid = accounts.GetAccountGuid();
        return ctx;
    }

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
        => Task.FromResult(CreateDbContext());
}
```

> Rationale: building `DbContextOptions` by hand and `new`-ing the context is the most reliable way to force the encryption constructor and per-instance `AccountGuid`, given EF's DI extensions don't forward custom ctor args. The health-check endpoint and `EnsureCreated` scope both resolve the scoped `AppDbContext`, which now carries encryption; `AccountGuid` there is null (no isolation needed for `CanConnect`/DDL).

- [ ] **Step 3: Build**

Run: `dotnet build ChildDev.Api/ChildDev.Api.csproj`
Expected: builds clean.

- [ ] **Step 4: Commit**

```bash
git add ChildDev.Api/Data/ScopedDbContextFactory.cs ChildDev.Api/Program.cs
git commit -m "feat(api): wire EncryptionService, account-scoped DbContext + factory"
```

---

## Task 6: Integration tests — isolation across web + API

**Files:**
- Test: `ChildDev.Api.Tests/TenantIsolationTests.cs`
- Reference existing: `ChildDev.Api.Tests/AuthEndpointTests.cs` for the `WebApplicationFactory` setup pattern (JWT secret + `CHILDDEV_ENC_KEY` must be injected via `ConfigureAppConfiguration`).

- [ ] **Step 1: Write failing tests**

Use the existing test factory pattern. Ensure config injects `CHILDDEV_ENC_KEY` (base64 32 bytes) and `CHILDDEV_JWT_SECRET`. Seed two accounts with goals via a scoped context using `IgnoreQueryFilters()`/explicit `AccountGuid`, then assert:

```csharp
using ChildDev.Api.Data;
using ChildDev.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChildDev.Api.Tests;

public class TenantIsolationTests : IClassFixture<TestAppFactory> // reuse/define per AuthEndpointTests
{
    private readonly TestAppFactory _f;
    public TenantIsolationTests(TestAppFactory f) => _f = f;

    [Fact]
    public async Task QueryFilter_ScopesGoalsToCurrentAccount()
    {
        using var scope = _f.Services.CreateScope();
        var enc = scope.ServiceProvider.GetRequiredService<EncryptionService>();
        var optsField = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(/* same conn as test */ TestConfig.Conn, ServerVersion.AutoDetect(TestConfig.Conn)).Options;

        // seed two tenants (filters off)
        using (var seed = new AppDbContext(optsField, enc))
        {
            seed.Goals.Add(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = "ACC-A", GoalText = "A secret", EnteredDate = 1, UpdatedOn = 1 });
            seed.Goals.Add(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = "ACC-B", GoalText = "B secret", EnteredDate = 1, UpdatedOn = 1 });
            await seed.SaveChangesAsync();
        }

        using var ctxA = new AppDbContext(optsField, enc) { AccountGuid = "ACC-A" };
        var goals = await ctxA.Goals.ToListAsync();
        Assert.All(goals, g => Assert.Equal("ACC-A", g.AccountFk));
        Assert.DoesNotContain(goals, g => g.AccountFk == "ACC-B");
    }

    [Fact]
    public async Task EncryptedColumn_IsCiphertextOnDisk_PlaintextInMemory()
    {
        using var scope = _f.Services.CreateScope();
        var enc = scope.ServiceProvider.GetRequiredService<EncryptionService>();
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(TestConfig.Conn, ServerVersion.AutoDetect(TestConfig.Conn)).Options;
        var guid = Guid.NewGuid().ToString();

        using (var w = new AppDbContext(opts, enc) { AccountGuid = "ACC-C" })
        {
            w.Goals.Add(new Goal { Guid = guid, AccountFk = "ACC-C", GoalText = "diary", EnteredDate = 1, UpdatedOn = 1 });
            await w.SaveChangesAsync();
        }
        // raw read bypassing converter
        using (var raw = new AppDbContext(opts) { AccountGuid = "ACC-C" })
        {
            var stored = await raw.Goals.Where(g => g.Guid == guid)
                .Select(g => g.GoalText).FirstAsync();
            Assert.StartsWith("v1:", stored);
        }
        using (var r = new AppDbContext(opts, enc) { AccountGuid = "ACC-C" })
        {
            var g = await r.Goals.FirstAsync(x => x.Guid == guid);
            Assert.Equal("diary", g.GoalText);
        }
    }
}
```

> If the test project uses an in-memory/SQLite provider rather than MySQL, adapt `UseMySql` to the existing test provider and skip the raw-ciphertext disk assertion on providers that don't support it; keep the round-trip + isolation assertions. Mirror whatever `AuthEndpointTests` already does.

- [ ] **Step 2: Run to verify it fails (or drives wiring fixes)**

Run: `dotnet test ChildDev.Api.Tests/ChildDev.Api.Tests.csproj --filter TenantIsolationTests`
Expected: FAIL initially if wiring incomplete; iterate until green.

- [ ] **Step 3: Make it pass** — fix wiring discovered by the tests. No new production code beyond Tasks 1–5 should be required.

- [ ] **Step 4: Run the FULL suite (regression guard for mobile-sync)**

Run: `dotnet test ChildDev.Api.Tests/ChildDev.Api.Tests.csproj`
Expected: ALL pass — especially existing JWT mobile-sync endpoint tests (proves the shared context still serves the API path via JWT account resolution).

- [ ] **Step 5: Commit**

```bash
git add ChildDev.Api.Tests/TenantIsolationTests.cs
git commit -m "test(api): tenant isolation + at-rest encryption integration tests"
```

---

## Task 7: Lazy migration hosted service

**Files:**
- Create: `ChildDev.Api/Services/EncryptionMigrationHostedService.cs`
- Modify: `ChildDev.Api/Program.cs` (register hosted service)
- Test: `ChildDev.Api.Tests/EncryptionMigrationTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Migration_EncryptsLegacyPlaintextRows()
{
    var enc = /* EncryptionService with test key */;
    var opts = /* AppDbContext options for test DB */;
    var guid = Guid.NewGuid().ToString();

    // write a legacy plaintext row using a context WITHOUT the converter
    using (var legacy = new AppDbContext(opts) { AccountGuid = "ACC-M" })
    {
        legacy.Goals.Add(new Goal { Guid = guid, AccountFk = "ACC-M", GoalText = "plain legacy", EnteredDate = 1, UpdatedOn = 1 });
        await legacy.SaveChangesAsync();
    }

    await EncryptionMigrationHostedService.RunOnceAsync(opts, enc, CancellationToken.None);

    using (var raw = new AppDbContext(opts) { AccountGuid = "ACC-M" })
    {
        var stored = await raw.Goals.IgnoreQueryFilters().Where(g => g.Guid == guid).Select(g => g.GoalText).FirstAsync();
        Assert.StartsWith("v1:", stored);
    }
    using (var r = new AppDbContext(opts, enc) { AccountGuid = "ACC-M" })
    {
        var g = await r.Goals.IgnoreQueryFilters().FirstAsync(x => x.Guid == guid);
        Assert.Equal("plain legacy", g.GoalText);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ChildDev.Api.Tests/ChildDev.Api.Tests.csproj --filter EncryptionMigrationTests`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Implement**

```csharp
using ChildDev.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Services;

/// <summary>One-shot, idempotent pass that re-saves rows whose encrypted columns are still
/// legacy plaintext, so they become ciphertext. Safe to run on every startup.</summary>
public sealed class EncryptionMigrationHostedService(
    IServiceProvider sp, ILogger<EncryptionMigrationHostedService> log) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            using var scope = sp.CreateScope();
            var enc = scope.ServiceProvider.GetRequiredService<EncryptionService>();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await RunOnceAsync(ctx, enc, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Encryption migration pass failed; will retry next startup.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    // Re-saves any Phase-1 row with a not-yet-encrypted target column. IgnoreQueryFilters
    // so it spans all tenants; AccountFk is plaintext so no decryption needed to enumerate.
    public static async Task RunOnceAsync(AppDbContext ctx, EncryptionService enc, CancellationToken ct)
    {
        const int batch = 200;

        await ReencryptAsync(ctx.Goals.IgnoreQueryFilters(),
            g => enc.IsEncrypted(/* already decrypted in memory; re-save always re-encrypts */ null) , ct, batch, ctx);
        // Simpler & correct: load in pages, re-save. The converter re-encrypts on save and is
        // idempotent because decrypt(plaintext)=plaintext, encrypt(plaintext)=v1:...
        // To avoid re-writing already-encrypted rows we filter on the raw column via a second
        // context without the converter.
    }
}
```

> IMPLEMENTATION NOTE: the cleanest idempotent approach is to enumerate with a **non-converter** context to see raw values, collect GUIDs whose target columns lack the `v1:` prefix, then load those rows in a **converter** context and `SaveChanges` (which encrypts). Concretely:

```csharp
    public static async Task RunOnceAsync(DbContextOptions<AppDbContext> opts, EncryptionService enc, CancellationToken ct)
    {
        await MigrateGoalsAsync(opts, enc, ct);
        await MigrateAsync<Models.Entities.Journal>(opts, enc, ct,
            raw => raw.Journals, sel: j => j.Notes, needs: v => v != null && !enc.IsEncrypted(v));
        await MigrateAsync<Models.Entities.GoalProgress>(opts, enc, ct,
            raw => raw.GoalProgresses, sel: p => p.NextStepItems, needs: v => v != null && !enc.IsEncrypted(v));
        await MigrateAsync<Models.Entities.Todo>(opts, enc, ct,
            raw => raw.Todos, sel: t => t.Notes, needs: v => v != null && !enc.IsEncrypted(v));
    }
```

Provide a generic helper that, for each entity: opens a raw `new AppDbContext(opts)` (no converter), finds keys where the selected column needs encryption, then opens a converter `new AppDbContext(opts, enc)`, loads those entities `IgnoreQueryFilters()`, and calls `SaveChanges()` (touch the entity so EF marks it modified — set the property to its current in-memory value). Goals iterate over `GoalText`, `MeasurableOutcome`, `Steps`.

> KEEP IT SIMPLE: if batching/raw-inspection proves fiddly, an acceptable equivalent is: load each entity in a converter context (decrypt-on-read makes legacy plaintext readable), mark all Phase-1 properties modified, `SaveChanges` (encrypt-on-write). Idempotent because re-encrypting already-`v1:` rows still yields valid `v1:` rows. The only cost is rewriting already-encrypted rows once per startup; gate with a `Preferences`/marker table flag if that cost matters. For correctness, the test in Step 1 must pass.

- [ ] **Step 4: Register in Program.cs**

In the startup scope block (after `EnsureCreated` and the `ALTER TABLE` statements, `Program.cs:136-149`), register the hosted service before `app.Run()`:
```csharp
builder.Services.AddHostedService<EncryptionMigrationHostedService>();
```
(Add this with the other `builder.Services` registrations, before `var app = builder.Build();`.)

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test ChildDev.Api.Tests/ChildDev.Api.Tests.csproj --filter EncryptionMigrationTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ChildDev.Api/Services/EncryptionMigrationHostedService.cs ChildDev.Api/Program.cs ChildDev.Api.Tests/EncryptionMigrationTests.cs
git commit -m "feat(api): one-shot lazy re-encryption migration of legacy plaintext rows"
```

---

## Task 8: Docs + env example

**Files:**
- Modify: `.env.example`
- Modify: `CLAUDE.md` (note the new constraint/key)

- [ ] **Step 1: Add to `.env.example`**

```
# Base64-encoded 32-byte AES key for at-rest column encryption.
# Real value lives ONLY in ~/data/.secrets/levelUp.enckey (gitignored), identical on dev+prod.
# Generate: openssl rand -base64 32
CHILDDEV_ENC_KEY=
```

- [ ] **Step 2: Note deployment wiring**

In `CLAUDE.md` under deployment/secrets, add one line: the prod container must export `CHILDDEV_ENC_KEY="$(cat ~/data/.secrets/levelUp.enckey)"` (or mount the file) — the app fails fast without it.

- [ ] **Step 3: Full suite + build**

Run: `dotnet build ChildDev.Api/ChildDev.Api.csproj && dotnet test ChildDev.Api.Tests/ChildDev.Api.Tests.csproj`
Expected: build clean, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add .env.example CLAUDE.md
git commit -m "docs: document CHILDDEV_ENC_KEY source and deployment wiring"
```

---

## Self-Review Notes

- **Spec coverage:** column encryption (T1,T2,T4), version-tag/legacy (T1), key from keyfile→env fail-fast (T5,T8), query filters incl. Account exclusion (T4), JWT-then-session provider (T3), factory wrapper (T5), lazy migration (T7), tests incl. mobile-sync regression (T6). All covered.
- **Deployment caveat:** wiring `CHILDDEV_ENC_KEY` into the prod compose env is an ops step (T8) — flagged in the spec as brushing the "no env edits" constraint; user authorized the keyfile.
- **Phase 2 (bounded columns):** out of scope here; enabled later via the existing `ALTER TABLE … MODIFY … LONGTEXT` pattern at `Program.cs:142-148` plus adding those properties to the converter list in T4.
