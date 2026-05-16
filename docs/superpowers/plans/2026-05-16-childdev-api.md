# ChildDev API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the ChildDev ASP.NET Core minimal API with MariaDB persistence, JWT auth, and batch sync endpoints for Journal, Goal, GoalProgress, and Todo entities.

**Architecture:** Minimal API project with EF Core 8 (Pomelo) for MariaDB. Auth issues JWTs using a shared secret. Sync endpoints receive a client batch + lastSyncAt timestamp, upsert using last-write-wins (higher UpdatedOn wins), and return the server delta. Integration tests use WebApplicationFactory with EF Core in-memory provider.

**Tech Stack:** .NET 8, ASP.NET Core Minimal API, EF Core 8, Pomelo.EntityFrameworkCore.MySql 8, xUnit, BCrypt.Net-Next, System.IdentityModel.Tokens.Jwt

---

## File Map

```
childDev/ChildDev.Api/
├── ChildDev.Api.csproj
├── Program.cs                          # App bootstrap, DI, middleware, route registration
├── Dockerfile
├── sql/init.sql                        # MariaDB DDL run on first container start
├── Models/
│   ├── Entities/
│   │   ├── Account.cs                  # EF entity: Guid, NickName, PinHash, CreatedOn
│   │   ├── Journal.cs                  # EF entity: all journal fields + SyncBase fields
│   │   ├── Goal.cs
│   │   ├── GoalProgress.cs
│   │   └── Todo.cs
│   └── Dtos/
│       ├── AuthDtos.cs                 # RegisterRequest, TokenRequest, AuthResponse
│       └── SyncDtos.cs                 # SyncRequest<T>, SyncResponse<T>, per-entity DTOs
├── Data/
│   └── AppDbContext.cs                 # EF Core DbContext, all DbSets, index config
├── Endpoints/
│   ├── AuthEndpoints.cs                # POST /api/auth/register, POST /api/auth/token
│   ├── JournalEndpoints.cs             # POST /api/sync/journal
│   ├── GoalEndpoints.cs                # POST /api/sync/goal
│   ├── GoalProgressEndpoints.cs        # POST /api/sync/goal-progress
│   └── TodoEndpoints.cs                # POST /api/sync/todo
└── Services/
    └── JwtService.cs                   # Issue + validate JWTs, extract accountGuid claim

childDev/ChildDev.Api.Tests/
├── ChildDev.Api.Tests.csproj
├── Helpers/
│   └── ApiFactory.cs                   # WebApplicationFactory with in-memory EF
├── AuthEndpointTests.cs
├── JournalSyncTests.cs
├── GoalSyncTests.cs
├── GoalProgressSyncTests.cs
└── TodoSyncTests.cs
```

---

## Task 1: Scaffold Projects

**Files:**
- Create: `childDev/ChildDev.Api/ChildDev.Api.csproj`
- Create: `childDev/ChildDev.Api.Tests/ChildDev.Api.Tests.csproj`

- [ ] **Step 1: Create the solution and projects**

Run from `/mnt/8TB_HDD_DATA/shared/src/childDev`:
```bash
dotnet new sln -n ChildDev
dotnet new webapi -n ChildDev.Api -o ChildDev.Api --use-minimal-apis
dotnet new xunit -n ChildDev.Api.Tests -o ChildDev.Api.Tests
dotnet sln add ChildDev.Api/ChildDev.Api.csproj
dotnet sln add ChildDev.Api.Tests/ChildDev.Api.Tests.csproj
```

- [ ] **Step 2: Add NuGet packages to API project**

```bash
cd ChildDev.Api
dotnet add package Pomelo.EntityFrameworkCore.MySql --version 8.0.2
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.15
dotnet add package BCrypt.Net-Next --version 4.0.3
dotnet add package System.IdentityModel.Tokens.Jwt --version 8.0.2
dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.0.15
```

- [ ] **Step 3: Add NuGet packages to test project**

```bash
cd ../ChildDev.Api.Tests
dotnet add package Microsoft.AspNetCore.Mvc.Testing --version 8.0.15
dotnet add package Microsoft.EntityFrameworkCore.InMemory --version 8.0.15
dotnet reference ../ChildDev.Api/ChildDev.Api.csproj
```

- [ ] **Step 4: Delete the generated scaffold files we won't use**

```bash
cd ../ChildDev.Api
rm -f Controllers/ -r 2>/dev/null || true
rm -f WeatherForecast.cs 2>/dev/null || true
```

- [ ] **Step 5: Verify projects build**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
dotnet build
```
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add ChildDev.Api/ ChildDev.Api.Tests/ ChildDev.sln
git commit -m "feat: scaffold ChildDev.Api and ChildDev.Api.Tests projects"
```

---

## Task 2: EF Core Entities and AppDbContext

**Files:**
- Create: `childDev/ChildDev.Api/Models/Entities/Account.cs`
- Create: `childDev/ChildDev.Api/Models/Entities/Journal.cs`
- Create: `childDev/ChildDev.Api/Models/Entities/Goal.cs`
- Create: `childDev/ChildDev.Api/Models/Entities/GoalProgress.cs`
- Create: `childDev/ChildDev.Api/Models/Entities/Todo.cs`
- Create: `childDev/ChildDev.Api/Data/AppDbContext.cs`

- [ ] **Step 1: Write the failing test for AppDbContext**

Create `childDev/ChildDev.Api.Tests/Helpers/ApiFactory.cs`:
```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ChildDev.Api.Data;

namespace ChildDev.Api.Tests.Helpers;

public class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase("TestDb_" + Guid.NewGuid()));
        });
    }
}
```

Create `childDev/ChildDev.Api.Tests/AuthEndpointTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using ChildDev.Api.Tests.Helpers;

namespace ChildDev.Api.Tests;

public class AuthEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Register_Returns201_WithJwt()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            NickName = "testuser",
            PinHash = "hashedpin123"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(body!["Jwt"]);
        Assert.NotNull(body["AccountGuid"]);
    }

    [Fact]
    public async Task Register_DuplicateNickName_Returns409()
    {
        var payload = new { NickName = "dupeuser", PinHash = "hashedpin123" };
        await _client.PostAsJsonAsync("/api/auth/register", payload);
        var response = await _client.PostAsJsonAsync("/api/auth/register", payload);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Token_ValidCredentials_Returns200_WithJwt()
    {
        var nick = "tokenuser";
        var pin = "hashedpin123";
        await _client.PostAsJsonAsync("/api/auth/register", new { NickName = nick, PinHash = pin });

        var response = await _client.PostAsJsonAsync("/api/auth/token", new
        {
            NickName = nick,
            PinHash = pin
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(body!["Jwt"]);
    }

    [Fact]
    public async Task Token_WrongPin_Returns401()
    {
        var nick = "authuser";
        await _client.PostAsJsonAsync("/api/auth/register", new { NickName = nick, PinHash = "correcthash" });

        var response = await _client.PostAsJsonAsync("/api/auth/token", new
        {
            NickName = nick,
            PinHash = "wronghash"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
dotnet test ChildDev.Api.Tests --filter "AuthEndpointTests"
```
Expected: FAIL — compilation errors because entity/context classes don't exist yet.

- [ ] **Step 3: Create entity classes**

Create `childDev/ChildDev.Api/Models/Entities/Account.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace ChildDev.Api.Models.Entities;

public class Account
{
    [Key, MaxLength(36)]
    public string Guid { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string NickName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string PinHash { get; set; } = string.Empty;

    public long CreatedOn { get; set; }
}
```

Create `childDev/ChildDev.Api/Models/Entities/Journal.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace ChildDev.Api.Models.Entities;

public class Journal
{
    [Key, MaxLength(36)]
    public string Guid { get; set; } = string.Empty;

    [Required, MaxLength(36)]
    public string AccountFk { get; set; } = string.Empty;

    public string? Notes { get; set; }

    [MaxLength(255)]
    public string? Activity { get; set; }

    [MaxLength(50)]
    public string? Mood { get; set; }

    [MaxLength(500)]
    public string? Tags { get; set; }

    public long EnteredDate { get; set; }
    public long UpdatedOn { get; set; }
    public long? DeletedAt { get; set; }
}
```

Create `childDev/ChildDev.Api/Models/Entities/Goal.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace ChildDev.Api.Models.Entities;

public class Goal
{
    [Key, MaxLength(36)]
    public string Guid { get; set; } = string.Empty;

    [Required, MaxLength(36)]
    public string AccountFk { get; set; } = string.Empty;

    public string? GoalText { get; set; }
    public long? NextMeetingDate { get; set; }
    public long? ExpirationDate { get; set; }
    public long EnteredDate { get; set; }
    public string? MeasurableOutcome { get; set; }
    public long? CompletionDate { get; set; }
    public long UpdatedOn { get; set; }
    public long? DeletedAt { get; set; }
}
```

Create `childDev/ChildDev.Api/Models/Entities/GoalProgress.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace ChildDev.Api.Models.Entities;

public class GoalProgress
{
    [Key, MaxLength(36)]
    public string Guid { get; set; } = string.Empty;

    [Required, MaxLength(36)]
    public string AccountFk { get; set; } = string.Empty;

    [Required, MaxLength(36)]
    public string GoalFk { get; set; } = string.Empty;

    public string? NextStepItems { get; set; }
    public long? NextMeetingDate { get; set; }
    public long UpdatedOn { get; set; }
    public long? DeletedAt { get; set; }
}
```

Create `childDev/ChildDev.Api/Models/Entities/Todo.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace ChildDev.Api.Models.Entities;

public class Todo
{
    [Key, MaxLength(36)]
    public string Guid { get; set; } = string.Empty;

    [Required, MaxLength(36)]
    public string AccountFk { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Title { get; set; }

    public string? Notes { get; set; }
    public long? DueDate { get; set; }
    public long? CompletedAt { get; set; }
    public long UpdatedOn { get; set; }
    public long? DeletedAt { get; set; }
}
```

Create `childDev/ChildDev.Api/Data/AppDbContext.cs`:
```csharp
using ChildDev.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Journal> Journals => Set<Journal>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<GoalProgress> GoalProgresses => Set<GoalProgress>();
    public DbSet<Todo> Todos => Set<Todo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Journal>()
            .HasIndex(j => new { j.AccountFk, j.UpdatedOn });

        modelBuilder.Entity<Goal>()
            .HasIndex(g => new { g.AccountFk, g.UpdatedOn });

        modelBuilder.Entity<GoalProgress>()
            .HasIndex(p => new { p.AccountFk, p.UpdatedOn });

        modelBuilder.Entity<Todo>()
            .HasIndex(t => new { t.AccountFk, t.UpdatedOn });

        modelBuilder.Entity<Account>()
            .HasIndex(a => a.NickName)
            .IsUnique();
    }
}
```

- [ ] **Step 4: Create the minimal Program.cs so tests can compile**

Replace the generated `childDev/ChildDev.Api/Program.cs`:
```csharp
using ChildDev.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration["CHILDDEV_DB_CONNECTION"]
    ?? "Server=localhost;Database=childdev;User=childdev;Password=dev;";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.Run();

public partial class Program { }
```

- [ ] **Step 5: Run tests — they should now fail on HTTP (no endpoints yet), not compile**

```bash
dotnet test ChildDev.Api.Tests --filter "AuthEndpointTests"
```
Expected: FAIL with `404 Not Found` (endpoints not wired yet), not compilation errors.

- [ ] **Step 6: Commit**

```bash
git add ChildDev.Api/ ChildDev.Api.Tests/
git commit -m "feat: add EF Core entities, AppDbContext, test scaffolding"
```

---

## Task 3: JWT Service

**Files:**
- Create: `childDev/ChildDev.Api/Services/JwtService.cs`
- Modify: `childDev/ChildDev.Api/Program.cs`

- [ ] **Step 1: Create JwtService**

Create `childDev/ChildDev.Api/Services/JwtService.cs`:
```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ChildDev.Api.Services;

public class JwtService(IConfiguration config)
{
    private readonly string _secret = config["CHILDDEV_JWT_SECRET"]
        ?? throw new InvalidOperationException("CHILDDEV_JWT_SECRET is not configured");

    public string Issue(string accountGuid)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: [new Claim("accountGuid", accountGuid)],
            expires: DateTime.UtcNow.AddDays(90),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string? ExtractAccountGuid(ClaimsPrincipal principal) =>
        principal.FindFirst("accountGuid")?.Value;
}
```

- [ ] **Step 2: Register JwtService and configure JWT bearer in Program.cs**

Replace `childDev/ChildDev.Api/Program.cs`:
```csharp
using System.Text;
using ChildDev.Api.Data;
using ChildDev.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration["CHILDDEV_DB_CONNECTION"]
    ?? "Server=localhost;Database=childdev;User=childdev;Password=dev;";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

builder.Services.AddSingleton<JwtService>();

var jwtSecret = builder.Configuration["CHILDDEV_JWT_SECRET"] ?? "dev-secret-min-32-chars-placeholder";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.Run();

public partial class Program { }
```

- [ ] **Step 3: Verify build**

```bash
dotnet build ChildDev.Api
```
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add ChildDev.Api/
git commit -m "feat: add JwtService with 90-day token issuance"
```

---

## Task 4: Auth Endpoints

**Files:**
- Create: `childDev/ChildDev.Api/Models/Dtos/AuthDtos.cs`
- Create: `childDev/ChildDev.Api/Endpoints/AuthEndpoints.cs`
- Modify: `childDev/ChildDev.Api/Program.cs`

- [ ] **Step 1: Create auth DTOs**

Create `childDev/ChildDev.Api/Models/Dtos/AuthDtos.cs`:
```csharp
namespace ChildDev.Api.Models.Dtos;

public record RegisterRequest(string NickName, string PinHash);
public record TokenRequest(string NickName, string PinHash);
public record AuthResponse(string Jwt, string AccountGuid);
```

- [ ] **Step 2: Create AuthEndpoints**

Create `childDev/ChildDev.Api/Endpoints/AuthEndpoints.cs`:
```csharp
using BCrypt.Net;
using ChildDev.Api.Data;
using ChildDev.Api.Models.Dtos;
using ChildDev.Api.Models.Entities;
using ChildDev.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/register", async (RegisterRequest req, AppDbContext db, JwtService jwt) =>
        {
            if (await db.Accounts.AnyAsync(a => a.NickName == req.NickName))
                return Results.Conflict("Nickname already taken");

            var account = new Account
            {
                Guid = Guid.NewGuid().ToString(),
                NickName = req.NickName,
                PinHash = BCrypt.Net.BCrypt.HashPassword(req.PinHash),
                CreatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            db.Accounts.Add(account);
            await db.SaveChangesAsync();

            return Results.Created($"/api/auth/{account.Guid}",
                new AuthResponse(jwt.Issue(account.Guid), account.Guid));
        });

        app.MapPost("/api/auth/token", async (TokenRequest req, AppDbContext db, JwtService jwt) =>
        {
            var account = await db.Accounts.FirstOrDefaultAsync(a => a.NickName == req.NickName);
            if (account is null || !BCrypt.Net.BCrypt.Verify(req.PinHash, account.PinHash))
                return Results.Unauthorized();

            return Results.Ok(new AuthResponse(jwt.Issue(account.Guid), account.Guid));
        });
    }
}
```

- [ ] **Step 3: Wire auth endpoints into Program.cs**

Add after `app.UseAuthorization();` in `childDev/ChildDev.Api/Program.cs`:
```csharp
app.MapAuthEndpoints();
```

Also add the using at the top:
```csharp
using ChildDev.Api.Endpoints;
```

Full updated `Program.cs`:
```csharp
using System.Text;
using ChildDev.Api.Data;
using ChildDev.Api.Endpoints;
using ChildDev.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration["CHILDDEV_DB_CONNECTION"]
    ?? "Server=localhost;Database=childdev;User=childdev;Password=dev;";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

builder.Services.AddSingleton<JwtService>();

var jwtSecret = builder.Configuration["CHILDDEV_JWT_SECRET"] ?? "dev-secret-min-32-chars-placeholder";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();

app.Run();

public partial class Program { }
```

- [ ] **Step 4: Run auth tests**

```bash
dotnet test ChildDev.Api.Tests --filter "AuthEndpointTests" -v
```
Expected: All 4 auth tests PASS.

- [ ] **Step 5: Commit**

```bash
git add ChildDev.Api/ ChildDev.Api.Tests/
git commit -m "feat: add auth register and token endpoints with BCrypt PIN verification"
```

---

## Task 5: Sync DTOs and Shared Sync Logic

**Files:**
- Create: `childDev/ChildDev.Api/Models/Dtos/SyncDtos.cs`

The sync pattern is identical for every entity — extract it once here, reference it in Tasks 6–9.

- [ ] **Step 1: Create sync DTOs**

Create `childDev/ChildDev.Api/Models/Dtos/SyncDtos.cs`:
```csharp
namespace ChildDev.Api.Models.Dtos;

public record SyncRequest<T>(List<T> Records, long LastSyncAt);
public record SyncResponse<T>(List<T> Records);

public record JournalDto(
    string Guid,
    string AccountFk,
    string? Notes,
    string? Activity,
    string? Mood,
    string? Tags,
    long EnteredDate,
    long UpdatedOn,
    long? DeletedAt);

public record GoalDto(
    string Guid,
    string AccountFk,
    string? GoalText,
    long? NextMeetingDate,
    long? ExpirationDate,
    long EnteredDate,
    string? MeasurableOutcome,
    long? CompletionDate,
    long UpdatedOn,
    long? DeletedAt);

public record GoalProgressDto(
    string Guid,
    string AccountFk,
    string GoalFk,
    string? NextStepItems,
    long? NextMeetingDate,
    long UpdatedOn,
    long? DeletedAt);

public record TodoDto(
    string Guid,
    string AccountFk,
    string? Title,
    string? Notes,
    long? DueDate,
    long? CompletedAt,
    long UpdatedOn,
    long? DeletedAt);
```

- [ ] **Step 2: Build**

```bash
dotnet build ChildDev.Api
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add ChildDev.Api/Models/Dtos/SyncDtos.cs
git commit -m "feat: add sync DTOs for all entity types"
```

---

## Task 6: Journal Sync Endpoint

**Files:**
- Create: `childDev/ChildDev.Api/Endpoints/JournalEndpoints.cs`
- Create: `childDev/ChildDev.Api.Tests/JournalSyncTests.cs`
- Modify: `childDev/ChildDev.Api/Program.cs`

- [ ] **Step 1: Write the failing test**

Create `childDev/ChildDev.Api.Tests/JournalSyncTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChildDev.Api.Models.Dtos;
using ChildDev.Api.Tests.Helpers;

namespace ChildDev.Api.Tests;

public class JournalSyncTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(string jwt, string accountGuid)> RegisterAsync(string nick = "juser")
    {
        var res = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(nick, "pinhash123"));
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        return (auth!.Jwt, auth.AccountGuid);
    }

    [Fact]
    public async Task Sync_EmptyBatch_Returns200_WithEmptyList()
    {
        var (jwt, _) = await RegisterAsync("jsync1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([], 0));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<JournalDto>>();
        Assert.Empty(body!.Records);
    }

    [Fact]
    public async Task Sync_NewRecord_ServerStoresIt_AndReturnsOnNextSync()
    {
        var (jwt, accountGuid) = await RegisterAsync("jsync2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid().ToString();
        var updatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var journal = new JournalDto(guid, accountGuid, "My note", null, null, null,
            updatedOn, updatedOn, null);

        await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([journal], 0));

        // Second sync from a different device (lastSyncAt = 0 again)
        var response2 = await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([], 0));

        var body = await response2.Content.ReadFromJsonAsync<SyncResponse<JournalDto>>();
        Assert.Single(body!.Records);
        Assert.Equal(guid, body.Records[0].Guid);
        Assert.Equal("My note", body.Records[0].Notes);
    }

    [Fact]
    public async Task Sync_ClientWinsWhenNewerUpdatedOn()
    {
        var (jwt, accountGuid) = await RegisterAsync("jsync3");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid().ToString();
        var t1 = 1000L;
        var t2 = 2000L;

        // Push old version
        await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>(
                [new JournalDto(guid, accountGuid, "old", null, null, null, t1, t1, null)], 0));

        // Push newer version
        await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>(
                [new JournalDto(guid, accountGuid, "new", null, null, null, t2, t2, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([], 0));

        var body = await response.Content.ReadFromJsonAsync<SyncResponse<JournalDto>>();
        Assert.Equal("new", body!.Records[0].Notes);
    }

    [Fact]
    public async Task Sync_ServerWinsWhenNewerUpdatedOn()
    {
        var (jwt, accountGuid) = await RegisterAsync("jsync4");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid().ToString();
        var t1 = 2000L;
        var t2 = 1000L; // older

        // Push newer version first
        await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>(
                [new JournalDto(guid, accountGuid, "server-wins", null, null, null, t1, t1, null)], 0));

        // Attempt to push older version
        await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>(
                [new JournalDto(guid, accountGuid, "client-stale", null, null, null, t2, t2, null)], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([], 0));

        var body = await response.Content.ReadFromJsonAsync<SyncResponse<JournalDto>>();
        Assert.Equal("server-wins", body!.Records[0].Notes);
    }

    [Fact]
    public async Task Sync_WithoutAuth_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.PostAsJsonAsync("/api/sync/journal",
            new SyncRequest<JournalDto>([], 0));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run to verify fail**

```bash
dotnet test ChildDev.Api.Tests --filter "JournalSyncTests" -v
```
Expected: FAIL (endpoints not defined).

- [ ] **Step 3: Implement JournalEndpoints**

Create `childDev/ChildDev.Api/Endpoints/JournalEndpoints.cs`:
```csharp
using System.Security.Claims;
using ChildDev.Api.Data;
using ChildDev.Api.Models.Dtos;
using ChildDev.Api.Models.Entities;
using ChildDev.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Endpoints;

public static class JournalEndpoints
{
    public static void MapJournalEndpoints(this WebApplication app)
    {
        app.MapPost("/api/sync/journal", async (
            SyncRequest<JournalDto> req,
            ClaimsPrincipal user,
            AppDbContext db,
            JwtService jwt) =>
        {
            var accountGuid = jwt.ExtractAccountGuid(user);
            if (accountGuid is null) return Results.Unauthorized();

            // Upsert client records
            foreach (var dto in req.Records)
            {
                if (dto.AccountFk != accountGuid) continue;

                var existing = await db.Journals.FindAsync(dto.Guid);
                if (existing is null)
                {
                    db.Journals.Add(DtoToEntity(dto));
                }
                else if (dto.UpdatedOn > existing.UpdatedOn)
                {
                    ApplyDto(existing, dto);
                }
            }
            await db.SaveChangesAsync();

            // Return server delta
            var delta = await db.Journals
                .Where(j => j.AccountFk == accountGuid && j.UpdatedOn > req.LastSyncAt)
                .Select(j => EntityToDto(j))
                .ToListAsync();

            return Results.Ok(new SyncResponse<JournalDto>(delta));
        }).RequireAuthorization();
    }

    private static Journal DtoToEntity(JournalDto dto) => new()
    {
        Guid = dto.Guid,
        AccountFk = dto.AccountFk,
        Notes = dto.Notes,
        Activity = dto.Activity,
        Mood = dto.Mood,
        Tags = dto.Tags,
        EnteredDate = dto.EnteredDate,
        UpdatedOn = dto.UpdatedOn,
        DeletedAt = dto.DeletedAt
    };

    private static void ApplyDto(Journal entity, JournalDto dto)
    {
        entity.Notes = dto.Notes;
        entity.Activity = dto.Activity;
        entity.Mood = dto.Mood;
        entity.Tags = dto.Tags;
        entity.EnteredDate = dto.EnteredDate;
        entity.UpdatedOn = dto.UpdatedOn;
        entity.DeletedAt = dto.DeletedAt;
    }

    private static JournalDto EntityToDto(Journal j) => new(
        j.Guid, j.AccountFk, j.Notes, j.Activity, j.Mood, j.Tags,
        j.EnteredDate, j.UpdatedOn, j.DeletedAt);
}
```

- [ ] **Step 4: Wire into Program.cs**

Add after `app.MapAuthEndpoints();`:
```csharp
app.MapJournalEndpoints();
```

- [ ] **Step 5: Run tests**

```bash
dotnet test ChildDev.Api.Tests --filter "JournalSyncTests" -v
```
Expected: All 5 journal sync tests PASS.

- [ ] **Step 6: Commit**

```bash
git add ChildDev.Api/ ChildDev.Api.Tests/
git commit -m "feat: add journal sync endpoint with last-write-wins upsert"
```

---

## Task 7: Goal and GoalProgress Sync Endpoints

**Files:**
- Create: `childDev/ChildDev.Api/Endpoints/GoalEndpoints.cs`
- Create: `childDev/ChildDev.Api/Endpoints/GoalProgressEndpoints.cs`
- Create: `childDev/ChildDev.Api.Tests/GoalSyncTests.cs`
- Create: `childDev/ChildDev.Api.Tests/GoalProgressSyncTests.cs`
- Modify: `childDev/ChildDev.Api/Program.cs`

- [ ] **Step 1: Write failing tests for goal sync**

Create `childDev/ChildDev.Api.Tests/GoalSyncTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChildDev.Api.Models.Dtos;
using ChildDev.Api.Tests.Helpers;

namespace ChildDev.Api.Tests;

public class GoalSyncTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(string jwt, string accountGuid)> RegisterAsync(string nick)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(nick, "pinhash123"));
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        return (auth!.Jwt, auth.AccountGuid);
    }

    [Fact]
    public async Task Sync_NewGoal_StoredAndReturned()
    {
        var (jwt, accountGuid) = await RegisterAsync("gsync1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new GoalDto(guid, accountGuid, "Learn to read", null, null, ts, null, null, ts, null);

        await _client.PostAsJsonAsync("/api/sync/goal", new SyncRequest<GoalDto>([goal], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalDto>>();

        Assert.Single(body!.Records);
        Assert.Equal("Learn to read", body.Records[0].GoalText);
    }

    [Fact]
    public async Task Sync_WithoutAuth_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.PostAsJsonAsync("/api/sync/goal",
            new SyncRequest<GoalDto>([], 0));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

Create `childDev/ChildDev.Api.Tests/GoalProgressSyncTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChildDev.Api.Models.Dtos;
using ChildDev.Api.Tests.Helpers;

namespace ChildDev.Api.Tests;

public class GoalProgressSyncTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(string jwt, string accountGuid)> RegisterAsync(string nick)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(nick, "pinhash123"));
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        return (auth!.Jwt, auth.AccountGuid);
    }

    [Fact]
    public async Task Sync_NewGoalProgress_StoredAndReturned()
    {
        var (jwt, accountGuid) = await RegisterAsync("gpsync1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid().ToString();
        var goalGuid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var progress = new GoalProgressDto(guid, accountGuid, goalGuid, "Step 1 done", null, ts, null);

        await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([progress], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<GoalProgressDto>>();

        Assert.Single(body!.Records);
        Assert.Equal("Step 1 done", body.Records[0].NextStepItems);
    }

    [Fact]
    public async Task Sync_WithoutAuth_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.PostAsJsonAsync("/api/sync/goal-progress",
            new SyncRequest<GoalProgressDto>([], 0));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run to verify fail**

```bash
dotnet test ChildDev.Api.Tests --filter "GoalSyncTests|GoalProgressSyncTests" -v
```
Expected: FAIL (endpoints not defined).

- [ ] **Step 3: Implement GoalEndpoints**

Create `childDev/ChildDev.Api/Endpoints/GoalEndpoints.cs`:
```csharp
using System.Security.Claims;
using ChildDev.Api.Data;
using ChildDev.Api.Models.Dtos;
using ChildDev.Api.Models.Entities;
using ChildDev.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Endpoints;

public static class GoalEndpoints
{
    public static void MapGoalEndpoints(this WebApplication app)
    {
        app.MapPost("/api/sync/goal", async (
            SyncRequest<GoalDto> req,
            ClaimsPrincipal user,
            AppDbContext db,
            JwtService jwt) =>
        {
            var accountGuid = jwt.ExtractAccountGuid(user);
            if (accountGuid is null) return Results.Unauthorized();

            foreach (var dto in req.Records)
            {
                if (dto.AccountFk != accountGuid) continue;
                var existing = await db.Goals.FindAsync(dto.Guid);
                if (existing is null)
                    db.Goals.Add(DtoToEntity(dto));
                else if (dto.UpdatedOn > existing.UpdatedOn)
                    ApplyDto(existing, dto);
            }
            await db.SaveChangesAsync();

            var delta = await db.Goals
                .Where(g => g.AccountFk == accountGuid && g.UpdatedOn > req.LastSyncAt)
                .Select(g => EntityToDto(g))
                .ToListAsync();

            return Results.Ok(new SyncResponse<GoalDto>(delta));
        }).RequireAuthorization();
    }

    private static Goal DtoToEntity(GoalDto dto) => new()
    {
        Guid = dto.Guid, AccountFk = dto.AccountFk, GoalText = dto.GoalText,
        NextMeetingDate = dto.NextMeetingDate, ExpirationDate = dto.ExpirationDate,
        EnteredDate = dto.EnteredDate, MeasurableOutcome = dto.MeasurableOutcome,
        CompletionDate = dto.CompletionDate, UpdatedOn = dto.UpdatedOn, DeletedAt = dto.DeletedAt
    };

    private static void ApplyDto(Goal entity, GoalDto dto)
    {
        entity.GoalText = dto.GoalText; entity.NextMeetingDate = dto.NextMeetingDate;
        entity.ExpirationDate = dto.ExpirationDate; entity.MeasurableOutcome = dto.MeasurableOutcome;
        entity.CompletionDate = dto.CompletionDate; entity.UpdatedOn = dto.UpdatedOn;
        entity.DeletedAt = dto.DeletedAt;
    }

    private static GoalDto EntityToDto(Goal g) => new(
        g.Guid, g.AccountFk, g.GoalText, g.NextMeetingDate, g.ExpirationDate,
        g.EnteredDate, g.MeasurableOutcome, g.CompletionDate, g.UpdatedOn, g.DeletedAt);
}
```

- [ ] **Step 4: Implement GoalProgressEndpoints**

Create `childDev/ChildDev.Api/Endpoints/GoalProgressEndpoints.cs`:
```csharp
using System.Security.Claims;
using ChildDev.Api.Data;
using ChildDev.Api.Models.Dtos;
using ChildDev.Api.Models.Entities;
using ChildDev.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Endpoints;

public static class GoalProgressEndpoints
{
    public static void MapGoalProgressEndpoints(this WebApplication app)
    {
        app.MapPost("/api/sync/goal-progress", async (
            SyncRequest<GoalProgressDto> req,
            ClaimsPrincipal user,
            AppDbContext db,
            JwtService jwt) =>
        {
            var accountGuid = jwt.ExtractAccountGuid(user);
            if (accountGuid is null) return Results.Unauthorized();

            foreach (var dto in req.Records)
            {
                if (dto.AccountFk != accountGuid) continue;
                var existing = await db.GoalProgresses.FindAsync(dto.Guid);
                if (existing is null)
                    db.GoalProgresses.Add(DtoToEntity(dto));
                else if (dto.UpdatedOn > existing.UpdatedOn)
                    ApplyDto(existing, dto);
            }
            await db.SaveChangesAsync();

            var delta = await db.GoalProgresses
                .Where(p => p.AccountFk == accountGuid && p.UpdatedOn > req.LastSyncAt)
                .Select(p => EntityToDto(p))
                .ToListAsync();

            return Results.Ok(new SyncResponse<GoalProgressDto>(delta));
        }).RequireAuthorization();
    }

    private static GoalProgress DtoToEntity(GoalProgressDto dto) => new()
    {
        Guid = dto.Guid, AccountFk = dto.AccountFk, GoalFk = dto.GoalFk,
        NextStepItems = dto.NextStepItems, NextMeetingDate = dto.NextMeetingDate,
        UpdatedOn = dto.UpdatedOn, DeletedAt = dto.DeletedAt
    };

    private static void ApplyDto(GoalProgress entity, GoalProgressDto dto)
    {
        entity.NextStepItems = dto.NextStepItems; entity.NextMeetingDate = dto.NextMeetingDate;
        entity.UpdatedOn = dto.UpdatedOn; entity.DeletedAt = dto.DeletedAt;
    }

    private static GoalProgressDto EntityToDto(GoalProgress p) => new(
        p.Guid, p.AccountFk, p.GoalFk, p.NextStepItems, p.NextMeetingDate, p.UpdatedOn, p.DeletedAt);
}
```

- [ ] **Step 5: Wire both into Program.cs**

Add after `app.MapJournalEndpoints();`:
```csharp
app.MapGoalEndpoints();
app.MapGoalProgressEndpoints();
```

Also add to usings:
```csharp
using ChildDev.Api.Endpoints;
```
(already present from Task 4)

- [ ] **Step 6: Run tests**

```bash
dotnet test ChildDev.Api.Tests --filter "GoalSyncTests|GoalProgressSyncTests" -v
```
Expected: All tests PASS.

- [ ] **Step 7: Commit**

```bash
git add ChildDev.Api/ ChildDev.Api.Tests/
git commit -m "feat: add goal and goal-progress sync endpoints"
```

---

## Task 8: Todo Sync Endpoint

**Files:**
- Create: `childDev/ChildDev.Api/Endpoints/TodoEndpoints.cs`
- Create: `childDev/ChildDev.Api.Tests/TodoSyncTests.cs`
- Modify: `childDev/ChildDev.Api/Program.cs`

- [ ] **Step 1: Write failing tests**

Create `childDev/ChildDev.Api.Tests/TodoSyncTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChildDev.Api.Models.Dtos;
using ChildDev.Api.Tests.Helpers;

namespace ChildDev.Api.Tests;

public class TodoSyncTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(string jwt, string accountGuid)> RegisterAsync(string nick)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(nick, "pinhash123"));
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        return (auth!.Jwt, auth.AccountGuid);
    }

    [Fact]
    public async Task Sync_NewTodo_StoredAndReturned()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new TodoDto(guid, accountGuid, "Buy groceries", null, null, null, ts, null);

        await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([todo], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();

        Assert.Single(body!.Records);
        Assert.Equal("Buy groceries", body.Records[0].Title);
    }

    [Fact]
    public async Task Sync_CompletedTodo_DeletedAtSet_ReturnedInDelta()
    {
        var (jwt, accountGuid) = await RegisterAsync("tsync2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var guid = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new TodoDto(guid, accountGuid, "Task", null, null, ts, ts, null);

        await _client.PostAsJsonAsync("/api/sync/todo", new SyncRequest<TodoDto>([todo], 0));

        var response = await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([], 0));
        var body = await response.Content.ReadFromJsonAsync<SyncResponse<TodoDto>>();

        Assert.Equal(ts, body!.Records[0].CompletedAt);
    }

    [Fact]
    public async Task Sync_WithoutAuth_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.PostAsJsonAsync("/api/sync/todo",
            new SyncRequest<TodoDto>([], 0));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run to verify fail**

```bash
dotnet test ChildDev.Api.Tests --filter "TodoSyncTests" -v
```
Expected: FAIL.

- [ ] **Step 3: Implement TodoEndpoints**

Create `childDev/ChildDev.Api/Endpoints/TodoEndpoints.cs`:
```csharp
using System.Security.Claims;
using ChildDev.Api.Data;
using ChildDev.Api.Models.Dtos;
using ChildDev.Api.Models.Entities;
using ChildDev.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Endpoints;

public static class TodoEndpoints
{
    public static void MapTodoEndpoints(this WebApplication app)
    {
        app.MapPost("/api/sync/todo", async (
            SyncRequest<TodoDto> req,
            ClaimsPrincipal user,
            AppDbContext db,
            JwtService jwt) =>
        {
            var accountGuid = jwt.ExtractAccountGuid(user);
            if (accountGuid is null) return Results.Unauthorized();

            foreach (var dto in req.Records)
            {
                if (dto.AccountFk != accountGuid) continue;
                var existing = await db.Todos.FindAsync(dto.Guid);
                if (existing is null)
                    db.Todos.Add(DtoToEntity(dto));
                else if (dto.UpdatedOn > existing.UpdatedOn)
                    ApplyDto(existing, dto);
            }
            await db.SaveChangesAsync();

            var delta = await db.Todos
                .Where(t => t.AccountFk == accountGuid && t.UpdatedOn > req.LastSyncAt)
                .Select(t => EntityToDto(t))
                .ToListAsync();

            return Results.Ok(new SyncResponse<TodoDto>(delta));
        }).RequireAuthorization();
    }

    private static Todo DtoToEntity(TodoDto dto) => new()
    {
        Guid = dto.Guid, AccountFk = dto.AccountFk, Title = dto.Title,
        Notes = dto.Notes, DueDate = dto.DueDate, CompletedAt = dto.CompletedAt,
        UpdatedOn = dto.UpdatedOn, DeletedAt = dto.DeletedAt
    };

    private static void ApplyDto(Todo entity, TodoDto dto)
    {
        entity.Title = dto.Title; entity.Notes = dto.Notes;
        entity.DueDate = dto.DueDate; entity.CompletedAt = dto.CompletedAt;
        entity.UpdatedOn = dto.UpdatedOn; entity.DeletedAt = dto.DeletedAt;
    }

    private static TodoDto EntityToDto(Todo t) => new(
        t.Guid, t.AccountFk, t.Title, t.Notes, t.DueDate, t.CompletedAt, t.UpdatedOn, t.DeletedAt);
}
```

- [ ] **Step 4: Wire into Program.cs**

Add after `app.MapGoalProgressEndpoints();`:
```csharp
app.MapTodoEndpoints();
```

- [ ] **Step 5: Run all tests**

```bash
dotnet test ChildDev.Api.Tests -v
```
Expected: All tests PASS.

- [ ] **Step 6: Commit**

```bash
git add ChildDev.Api/ ChildDev.Api.Tests/
git commit -m "feat: add todo sync endpoint — all API sync endpoints complete"
```

---

## Task 9: MariaDB Init SQL and Dockerfile

**Files:**
- Create: `childDev/ChildDev.Api/sql/init.sql`
- Create: `childDev/ChildDev.Api/Dockerfile`

- [ ] **Step 1: Create init.sql**

Create `childDev/ChildDev.Api/sql/init.sql`:
```sql
CREATE TABLE IF NOT EXISTS Account (
    Guid        CHAR(36) PRIMARY KEY,
    NickName    VARCHAR(100) NOT NULL,
    PinHash     VARCHAR(100) NOT NULL,
    CreatedOn   BIGINT NOT NULL,
    UNIQUE INDEX idx_account_nickname (NickName)
);

CREATE TABLE IF NOT EXISTS Journal (
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

CREATE TABLE IF NOT EXISTS Goal (
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

CREATE TABLE IF NOT EXISTS GoalProgress (
    Guid             CHAR(36) PRIMARY KEY,
    AccountFk        CHAR(36) NOT NULL,
    GoalFk           CHAR(36) NOT NULL,
    NextStepItems    TEXT,
    NextMeetingDate  BIGINT,
    UpdatedOn        BIGINT NOT NULL,
    DeletedAt        BIGINT,
    INDEX idx_goalprogress_account_updated (AccountFk, UpdatedOn)
);

CREATE TABLE IF NOT EXISTS Todo (
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

- [ ] **Step 2: Create Dockerfile**

Create `childDev/ChildDev.Api/Dockerfile`:
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ChildDev.Api.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "ChildDev.Api.dll"]
```

- [ ] **Step 3: Add EF Core database migration support to Program.cs**

Add auto-migration on startup (dev-friendly, suitable for this scale). Add before `app.Run()` in `Program.cs`:
```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}
```

- [ ] **Step 4: Run full test suite**

```bash
dotnet test ChildDev.Api.Tests -v
```
Expected: All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add ChildDev.Api/
git commit -m "feat: add MariaDB init.sql, Dockerfile, and EnsureCreated startup"
```

---

## Task 10: docker-compose-beta.yml and Secrets Template

**Files:**
- Create: `/mnt/8TB_HDD_DATA/shared/src/docker/docker-compose-beta.yml`
- Create: `childDev/ChildDev.Api/childdev-beta.env.example`

- [ ] **Step 1: Create docker-compose-beta.yml**

Create `/mnt/8TB_HDD_DATA/shared/src/docker/docker-compose-beta.yml`:
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
      - ASPNETCORE_URLS=http://+:8080
    networks:
      - beta_default
    restart: unless-stopped
    depends_on:
      childdev-db:
        condition: service_healthy

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
    healthcheck:
      test: ["CMD", "healthcheck.sh", "--connect", "--innodb_initialized"]
      interval: 10s
      timeout: 5s
      retries: 5

networks:
  beta_default:
    driver: bridge

volumes:
  childdev_db_data:
```

- [ ] **Step 2: Create secrets template (not committed to git)**

Create `childDev/ChildDev.Api/childdev-beta.env.example`:
```bash
# Copy to /home/jaxtrx/data/.secrets/childdev-beta.env and fill in values
MARIADB_ROOT_PASSWORD=changeme
MARIADB_DATABASE=childdev
MARIADB_USER=childdev
MARIADB_PASSWORD=changeme
CHILDDEV_DB_CONNECTION=Server=childdev-db;Database=childdev;User=childdev;Password=changeme;
CHILDDEV_JWT_SECRET=replace-with-at-least-32-random-characters-here
```

- [ ] **Step 3: Verify compose file parses**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/docker
docker compose -f docker-compose-beta.yml config --quiet
```
Expected: No errors (may warn about missing env file — that's fine).

- [ ] **Step 4: Add .env.example to gitignore and commit**

In `childDev/.gitignore` (create if absent), ensure:
```
*.env
!*.env.example
```

```bash
git add /mnt/8TB_HDD_DATA/shared/src/docker/docker-compose-beta.yml
git add childDev/ChildDev.Api/childdev-beta.env.example
git commit -m "feat: add docker-compose-beta.yml and secrets template for ChildDev API"
```

---

## Self-Review Checklist (run after writing — fix inline)

- [x] Auth register/token — covered Tasks 4
- [x] Journal sync — covered Task 6
- [x] Goal sync — covered Task 7
- [x] GoalProgress sync — covered Task 7
- [x] Todo sync — covered Task 8
- [x] MariaDB schema — covered Task 9 (init.sql)
- [x] Docker — covered Task 10
- [x] Secrets not committed — .gitignore + .env.example pattern
- [x] JWT 90-day expiry — JwtService Task 3
- [x] Last-write-wins on UpdatedOn — tested in JournalSyncTests Tasks 6
- [x] AccountFk guard on sync (can't write other users' data) — in all endpoint implementations
- [x] No TBD/TODO placeholders — verified
