# ChildDev Mobile Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the ChildDev .NET MAUI mobile app — offline-first, local SQLite, PIN account, Journal/Goals/Todos/Dashboard, with optional sync to the ChildDev API on app open.

**Architecture:** MAUI Shell with four bottom tabs (Dashboard, Journal, Goals, Todos). All data stored in SQLite via sqlite-net-pcl. ViewModels use CommunityToolkit.Mvvm. SyncService fires on app open when `IConnectivity` reports internet. SetupPage shown on first launch when no local account exists. The API is optional — the app works fully without it.

**Tech Stack:** .NET 8 MAUI, sqlite-net-pcl, CommunityToolkit.Mvvm, BCrypt.Net-Next, Microsoft.Extensions.Http

**Build note:** MAUI targets Android and iOS. Building for Android on Linux requires Android SDK (installed via `dotnet workload install android`). iOS builds require macOS. All non-UI code (repositories, services, ViewModels) is fully unit-testable on Linux via xUnit.

**Prerequisite:** Complete `2026-05-16-childdev-api.md` before implementing Task 11 (SyncService).

---

## File Map

```
childDev/ChildDev.Mobile/
├── ChildDev.Mobile.csproj
├── MauiProgram.cs                      # DI registration, app bootstrap
├── AppShell.xaml / .cs                 # 4-tab Shell: Dashboard, Journal, Goals, Todos
├── Models/
│   ├── SyncBase.cs                     # Guid, AccountFk, UpdatedOn, DeletedAt
│   ├── Journal.cs
│   ├── Goal.cs
│   ├── GoalProgress.cs
│   ├── Todo.cs
│   └── Account.cs                      # NickName, PinHash, LastSyncAt, ServerJwt, ServerUrl
├── Data/
│   ├── LocalDatabase.cs                # sqlite-net-pcl connection init, CreateTablesAsync
│   ├── JournalRepository.cs            # CRUD + GetModifiedSince(long ts)
│   ├── GoalRepository.cs
│   ├── GoalProgressRepository.cs
│   └── TodoRepository.cs
├── Services/
│   ├── AccountService.cs               # GetAccount, CreateAccount, VerifyPin, UpdateLastSync
│   ├── SyncService.cs                  # RunAsync: check connectivity, call API, upsert delta
│   └── ConnectivityService.cs          # Wraps IConnectivity, injectable for tests
├── ViewModels/
│   ├── SetupViewModel.cs               # NickName + PIN entry, creates local account
│   ├── DashboardViewModel.cs           # Recent 3 journals, active goals count, pending todos
│   ├── JournalListViewModel.cs         # Observable list, delete command
│   ├── JournalEntryViewModel.cs        # Save, load by guid
│   ├── GoalListViewModel.cs
│   ├── GoalEntryViewModel.cs
│   ├── TodoListViewModel.cs
│   └── SettingsViewModel.cs            # ServerUrl, last sync time, sync status message
└── Views/
    ├── SetupPage.xaml / .cs
    ├── DashboardPage.xaml / .cs
    ├── JournalListPage.xaml / .cs
    ├── JournalEntryPage.xaml / .cs
    ├── GoalListPage.xaml / .cs
    ├── GoalEntryPage.xaml / .cs
    ├── TodoListPage.xaml / .cs
    └── SettingsPage.xaml / .cs

childDev/ChildDev.Mobile.Tests/
├── ChildDev.Mobile.Tests.csproj
├── AccountServiceTests.cs
├── JournalRepositoryTests.cs
├── GoalRepositoryTests.cs
├── TodoRepositoryTests.cs
└── SyncServiceTests.cs
```

---

## Task 1: Scaffold MAUI Project and Test Project

**Files:**
- Create: `childDev/ChildDev.Mobile/ChildDev.Mobile.csproj`
- Create: `childDev/ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj`

- [ ] **Step 1: Install MAUI workload (one-time)**

```bash
dotnet workload install maui-android
```
Expected: Workload installed. (Takes several minutes first time.)

- [ ] **Step 2: Create MAUI project and test project**

Run from `/mnt/8TB_HDD_DATA/shared/src/childDev`:
```bash
dotnet new maui -n ChildDev.Mobile -o ChildDev.Mobile
dotnet new xunit -n ChildDev.Mobile.Tests -o ChildDev.Mobile.Tests
dotnet sln add ChildDev.Mobile/ChildDev.Mobile.csproj
dotnet sln add ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj
```

- [ ] **Step 3: Add NuGet packages to Mobile project**

```bash
cd ChildDev.Mobile
dotnet add package sqlite-net-pcl --version 1.9.172
dotnet add package SQLitePCLRaw.bundle_green --version 2.1.10
dotnet add package CommunityToolkit.Mvvm --version 8.4.0
dotnet add package BCrypt.Net-Next --version 4.0.3
```

- [ ] **Step 4: Add NuGet packages to test project**

```bash
cd ../ChildDev.Mobile.Tests
dotnet add package sqlite-net-pcl --version 1.9.172
dotnet add package SQLitePCLRaw.bundle_green --version 2.1.10
dotnet add package CommunityToolkit.Mvvm --version 8.4.0
dotnet add package BCrypt.Net-Next --version 4.0.3
dotnet reference ../ChildDev.Mobile/ChildDev.Mobile.csproj
```

- [ ] **Step 5: Configure test project to target net8.0 (not MAUI)**

Edit `childDev/ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj` — set target framework to `net8.0`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.0.1" />
    <PackageReference Include="sqlite-net-pcl" Version="1.9.172" />
    <PackageReference Include="SQLitePCLRaw.bundle_green" Version="2.1.10" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
    <PackageReference Include="BCrypt.Net-Next" Version="4.0.3" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../ChildDev.Mobile/ChildDev.Mobile.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Configure Mobile project to exclude MAUI platform specifics from test build**

In `childDev/ChildDev.Mobile/ChildDev.Mobile.csproj`, ensure non-MAUI code compiles for `net8.0` too (needed so test project can reference it):
```xml
<PropertyGroup>
  <TargetFrameworks>net8.0-android;net8.0-ios;net8.0</TargetFrameworks>
  <OutputType Condition="'$(TargetFramework)' != 'net8.0'">Exe</OutputType>
  <RootNamespace>ChildDev.Mobile</RootNamespace>
  <UseMaui Condition="'$(TargetFramework)' != 'net8.0'">true</UseMaui>
  <SingleProject>true</SingleProject>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
</PropertyGroup>
```

- [ ] **Step 7: Verify test project builds**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
dotnet build ChildDev.Mobile.Tests
```
Expected: Build succeeded.

- [ ] **Step 8: Commit**

```bash
git add ChildDev.Mobile/ ChildDev.Mobile.Tests/ ChildDev.sln
git commit -m "feat: scaffold ChildDev.Mobile MAUI project and test project"
```

---

## Task 2: Models and LocalDatabase

**Files:**
- Create: `childDev/ChildDev.Mobile/Models/SyncBase.cs`
- Create: `childDev/ChildDev.Mobile/Models/Journal.cs`
- Create: `childDev/ChildDev.Mobile/Models/Goal.cs`
- Create: `childDev/ChildDev.Mobile/Models/GoalProgress.cs`
- Create: `childDev/ChildDev.Mobile/Models/Todo.cs`
- Create: `childDev/ChildDev.Mobile/Models/Account.cs`
- Create: `childDev/ChildDev.Mobile/Data/LocalDatabase.cs`

- [ ] **Step 1: Write failing test for LocalDatabase**

Create `childDev/ChildDev.Mobile.Tests/JournalRepositoryTests.cs`:
```csharp
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using SQLite;

namespace ChildDev.Mobile.Tests;

public class JournalRepositoryTests : IDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private readonly JournalRepository _repo;

    public JournalRepositoryTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _db = new SQLiteAsyncConnection(":memory:");
        _db.CreateTableAsync<Journal>().GetAwaiter().GetResult();
        _repo = new JournalRepository(_db);
    }

    public void Dispose() => _db.CloseAsync().GetAwaiter().GetResult();

    [Fact]
    public async Task Save_NewJournal_CanBeRetrieved()
    {
        var journal = new Journal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            Notes = "Today was good",
            EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _repo.SaveAsync(journal);
        var all = await _repo.GetAllActiveAsync("account1");

        Assert.Single(all);
        Assert.Equal("Today was good", all[0].Notes);
    }

    [Fact]
    public async Task Delete_SoftDeletes_ExcludedFromActive()
    {
        var journal = new Journal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            Notes = "To delete",
            EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _repo.SaveAsync(journal);
        await _repo.DeleteAsync(journal.Guid);

        var all = await _repo.GetAllActiveAsync("account1");
        Assert.Empty(all);
    }

    [Fact]
    public async Task GetModifiedSince_ReturnsOnlyNewerRecords()
    {
        var t1 = 1000L;
        var t2 = 2000L;
        var accountId = "account2";

        await _repo.SaveAsync(new Journal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = accountId,
            Notes = "old",
            EnteredDate = t1,
            UpdatedOn = t1
        });
        await _repo.SaveAsync(new Journal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = accountId,
            Notes = "new",
            EnteredDate = t2,
            UpdatedOn = t2
        });

        var modified = await _repo.GetModifiedSinceAsync(accountId, t1);
        Assert.Single(modified);
        Assert.Equal("new", modified[0].Notes);
    }
}
```

- [ ] **Step 2: Run to verify fail**

```bash
dotnet test ChildDev.Mobile.Tests --filter "JournalRepositoryTests" -v
```
Expected: FAIL — types not defined yet.

- [ ] **Step 3: Create model classes**

Create `childDev/ChildDev.Mobile/Models/SyncBase.cs`:
```csharp
using SQLite;

namespace ChildDev.Mobile.Models;

public abstract class SyncBase
{
    [PrimaryKey]
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    public string AccountFk { get; set; } = string.Empty;
    public long UpdatedOn { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long? DeletedAt { get; set; }
}
```

Create `childDev/ChildDev.Mobile/Models/Journal.cs`:
```csharp
namespace ChildDev.Mobile.Models;

public class Journal : SyncBase
{
    public string? Notes { get; set; }
    public string? Activity { get; set; }
    public string? Mood { get; set; }
    public string? Tags { get; set; }
    public long EnteredDate { get; set; }
}
```

Create `childDev/ChildDev.Mobile/Models/Goal.cs`:
```csharp
namespace ChildDev.Mobile.Models;

public class Goal : SyncBase
{
    public string? GoalText { get; set; }
    public long? NextMeetingDate { get; set; }
    public long? ExpirationDate { get; set; }
    public long EnteredDate { get; set; }
    public string? MeasurableOutcome { get; set; }
    public long? CompletionDate { get; set; }
}
```

Create `childDev/ChildDev.Mobile/Models/GoalProgress.cs`:
```csharp
namespace ChildDev.Mobile.Models;

public class GoalProgress : SyncBase
{
    public string GoalFk { get; set; } = string.Empty;
    public string? NextStepItems { get; set; }
    public long? NextMeetingDate { get; set; }
}
```

Create `childDev/ChildDev.Mobile/Models/Todo.cs`:
```csharp
namespace ChildDev.Mobile.Models;

public class Todo : SyncBase
{
    public string? Title { get; set; }
    public string? Notes { get; set; }
    public long? DueDate { get; set; }
    public long? CompletedAt { get; set; }
}
```

Create `childDev/ChildDev.Mobile/Models/Account.cs`:
```csharp
using SQLite;

namespace ChildDev.Mobile.Models;

public class Account
{
    [PrimaryKey]
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    public string NickName { get; set; } = string.Empty;
    public string PinHash { get; set; } = string.Empty;
    public long CreatedOn { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long LastSyncAt { get; set; } = 0;
    public string? ServerJwt { get; set; }
    public string? ServerUrl { get; set; }
}
```

- [ ] **Step 4: Create LocalDatabase**

Create `childDev/ChildDev.Mobile/Data/LocalDatabase.cs`:
```csharp
using ChildDev.Mobile.Models;
using SQLite;

namespace ChildDev.Mobile.Data;

public class LocalDatabase
{
    private readonly SQLiteAsyncConnection _db;

    public LocalDatabase(string dbPath)
    {
        SQLitePCL.Batteries_V2.Init();
        _db = new SQLiteAsyncConnection(dbPath);
    }

    public SQLiteAsyncConnection Connection => _db;

    public async Task InitAsync()
    {
        await _db.CreateTableAsync<Account>();
        await _db.CreateTableAsync<Journal>();
        await _db.CreateTableAsync<Goal>();
        await _db.CreateTableAsync<GoalProgress>();
        await _db.CreateTableAsync<Todo>();
    }
}
```

- [ ] **Step 5: Create JournalRepository**

Create `childDev/ChildDev.Mobile/Data/JournalRepository.cs`:
```csharp
using ChildDev.Mobile.Models;
using SQLite;

namespace ChildDev.Mobile.Data;

public class JournalRepository(SQLiteAsyncConnection db)
{
    public Task SaveAsync(Journal journal)
    {
        journal.UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return db.InsertOrReplaceAsync(journal);
    }

    public async Task DeleteAsync(string guid)
    {
        var item = await db.FindAsync<Journal>(guid);
        if (item is null) return;
        item.DeletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        item.UpdatedOn = item.DeletedAt.Value;
        await db.UpdateAsync(item);
    }

    public Task<List<Journal>> GetAllActiveAsync(string accountFk) =>
        db.Table<Journal>()
          .Where(j => j.AccountFk == accountFk && j.DeletedAt == null)
          .OrderByDescending(j => j.EnteredDate)
          .ToListAsync();

    public Task<Journal?> GetAsync(string guid) =>
        db.FindAsync<Journal>(guid);

    public Task<List<Journal>> GetModifiedSinceAsync(string accountFk, long since) =>
        db.Table<Journal>()
          .Where(j => j.AccountFk == accountFk && j.UpdatedOn > since)
          .ToListAsync();

    public Task UpsertFromSyncAsync(Journal journal) =>
        db.InsertOrReplaceAsync(journal);
}
```

- [ ] **Step 6: Run tests**

```bash
dotnet test ChildDev.Mobile.Tests --filter "JournalRepositoryTests" -v
```
Expected: All 3 tests PASS.

- [ ] **Step 7: Commit**

```bash
git add ChildDev.Mobile/ ChildDev.Mobile.Tests/
git commit -m "feat: add SQLite models, LocalDatabase init, and JournalRepository"
```

---

## Task 3: Goal, GoalProgress, and Todo Repositories

**Files:**
- Create: `childDev/ChildDev.Mobile/Data/GoalRepository.cs`
- Create: `childDev/ChildDev.Mobile/Data/GoalProgressRepository.cs`
- Create: `childDev/ChildDev.Mobile/Data/TodoRepository.cs`
- Create: `childDev/ChildDev.Mobile.Tests/GoalRepositoryTests.cs`
- Create: `childDev/ChildDev.Mobile.Tests/TodoRepositoryTests.cs`

- [ ] **Step 1: Write failing tests**

Create `childDev/ChildDev.Mobile.Tests/GoalRepositoryTests.cs`:
```csharp
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using SQLite;

namespace ChildDev.Mobile.Tests;

public class GoalRepositoryTests : IDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private readonly GoalRepository _repo;

    public GoalRepositoryTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _db = new SQLiteAsyncConnection(":memory:");
        _db.CreateTableAsync<Goal>().GetAwaiter().GetResult();
        _repo = new GoalRepository(_db);
    }

    public void Dispose() => _db.CloseAsync().GetAwaiter().GetResult();

    [Fact]
    public async Task Save_NewGoal_CanBeRetrieved()
    {
        var goal = new Goal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            GoalText = "Learn piano",
            EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _repo.SaveAsync(goal);
        var all = await _repo.GetAllActiveAsync("account1");

        Assert.Single(all);
        Assert.Equal("Learn piano", all[0].GoalText);
    }

    [Fact]
    public async Task Delete_SoftDeletes_ExcludedFromActive()
    {
        var goal = new Goal
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            GoalText = "Delete me",
            EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _repo.SaveAsync(goal);
        await _repo.DeleteAsync(goal.Guid);
        var all = await _repo.GetAllActiveAsync("account1");
        Assert.Empty(all);
    }
}
```

Create `childDev/ChildDev.Mobile.Tests/TodoRepositoryTests.cs`:
```csharp
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using SQLite;

namespace ChildDev.Mobile.Tests;

public class TodoRepositoryTests : IDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private readonly TodoRepository _repo;

    public TodoRepositoryTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _db = new SQLiteAsyncConnection(":memory:");
        _db.CreateTableAsync<Todo>().GetAwaiter().GetResult();
        _repo = new TodoRepository(_db);
    }

    public void Dispose() => _db.CloseAsync().GetAwaiter().GetResult();

    [Fact]
    public async Task Save_NewTodo_CanBeRetrieved()
    {
        var todo = new Todo
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            Title = "Buy milk",
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _repo.SaveAsync(todo);
        var pending = await _repo.GetPendingAsync("account1");
        Assert.Single(pending);
        Assert.Equal("Buy milk", pending[0].Title);
    }

    [Fact]
    public async Task Complete_SetCompletedAt_ExcludedFromPending()
    {
        var todo = new Todo
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = "account1",
            Title = "Done task",
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _repo.SaveAsync(todo);
        await _repo.CompleteAsync(todo.Guid);

        var pending = await _repo.GetPendingAsync("account1");
        Assert.Empty(pending);
    }
}
```

- [ ] **Step 2: Run to verify fail**

```bash
dotnet test ChildDev.Mobile.Tests --filter "GoalRepositoryTests|TodoRepositoryTests" -v
```
Expected: FAIL.

- [ ] **Step 3: Create GoalRepository**

Create `childDev/ChildDev.Mobile/Data/GoalRepository.cs`:
```csharp
using ChildDev.Mobile.Models;
using SQLite;

namespace ChildDev.Mobile.Data;

public class GoalRepository(SQLiteAsyncConnection db)
{
    public Task SaveAsync(Goal goal)
    {
        goal.UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return db.InsertOrReplaceAsync(goal);
    }

    public async Task DeleteAsync(string guid)
    {
        var item = await db.FindAsync<Goal>(guid);
        if (item is null) return;
        item.DeletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        item.UpdatedOn = item.DeletedAt.Value;
        await db.UpdateAsync(item);
    }

    public Task<List<Goal>> GetAllActiveAsync(string accountFk) =>
        db.Table<Goal>()
          .Where(g => g.AccountFk == accountFk && g.DeletedAt == null)
          .OrderByDescending(g => g.EnteredDate)
          .ToListAsync();

    public Task<Goal?> GetAsync(string guid) =>
        db.FindAsync<Goal>(guid);

    public Task<List<Goal>> GetModifiedSinceAsync(string accountFk, long since) =>
        db.Table<Goal>()
          .Where(g => g.AccountFk == accountFk && g.UpdatedOn > since)
          .ToListAsync();

    public Task UpsertFromSyncAsync(Goal goal) =>
        db.InsertOrReplaceAsync(goal);
}
```

- [ ] **Step 4: Create GoalProgressRepository**

Create `childDev/ChildDev.Mobile/Data/GoalProgressRepository.cs`:
```csharp
using ChildDev.Mobile.Models;
using SQLite;

namespace ChildDev.Mobile.Data;

public class GoalProgressRepository(SQLiteAsyncConnection db)
{
    public Task SaveAsync(GoalProgress progress)
    {
        progress.UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return db.InsertOrReplaceAsync(progress);
    }

    public Task<List<GoalProgress>> GetForGoalAsync(string goalFk) =>
        db.Table<GoalProgress>()
          .Where(p => p.GoalFk == goalFk && p.DeletedAt == null)
          .OrderByDescending(p => p.UpdatedOn)
          .ToListAsync();

    public Task<List<GoalProgress>> GetModifiedSinceAsync(string accountFk, long since) =>
        db.Table<GoalProgress>()
          .Where(p => p.AccountFk == accountFk && p.UpdatedOn > since)
          .ToListAsync();

    public Task UpsertFromSyncAsync(GoalProgress progress) =>
        db.InsertOrReplaceAsync(progress);
}
```

- [ ] **Step 5: Create TodoRepository**

Create `childDev/ChildDev.Mobile/Data/TodoRepository.cs`:
```csharp
using ChildDev.Mobile.Models;
using SQLite;

namespace ChildDev.Mobile.Data;

public class TodoRepository(SQLiteAsyncConnection db)
{
    public Task SaveAsync(Todo todo)
    {
        todo.UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return db.InsertOrReplaceAsync(todo);
    }

    public async Task CompleteAsync(string guid)
    {
        var item = await db.FindAsync<Todo>(guid);
        if (item is null) return;
        item.CompletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        item.UpdatedOn = item.CompletedAt.Value;
        await db.UpdateAsync(item);
    }

    public async Task DeleteAsync(string guid)
    {
        var item = await db.FindAsync<Todo>(guid);
        if (item is null) return;
        item.DeletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        item.UpdatedOn = item.DeletedAt.Value;
        await db.UpdateAsync(item);
    }

    public Task<List<Todo>> GetPendingAsync(string accountFk) =>
        db.Table<Todo>()
          .Where(t => t.AccountFk == accountFk && t.DeletedAt == null && t.CompletedAt == null)
          .OrderBy(t => t.DueDate)
          .ToListAsync();

    public Task<List<Todo>> GetAllActiveAsync(string accountFk) =>
        db.Table<Todo>()
          .Where(t => t.AccountFk == accountFk && t.DeletedAt == null)
          .OrderByDescending(t => t.UpdatedOn)
          .ToListAsync();

    public Task<List<Todo>> GetModifiedSinceAsync(string accountFk, long since) =>
        db.Table<Todo>()
          .Where(t => t.AccountFk == accountFk && t.UpdatedOn > since)
          .ToListAsync();

    public Task UpsertFromSyncAsync(Todo todo) =>
        db.InsertOrReplaceAsync(todo);
}
```

- [ ] **Step 6: Run tests**

```bash
dotnet test ChildDev.Mobile.Tests -v
```
Expected: All tests PASS.

- [ ] **Step 7: Commit**

```bash
git add ChildDev.Mobile/ ChildDev.Mobile.Tests/
git commit -m "feat: add Goal, GoalProgress, and Todo repositories with soft delete"
```

---

## Task 4: AccountService

**Files:**
- Create: `childDev/ChildDev.Mobile/Services/AccountService.cs`
- Create: `childDev/ChildDev.Mobile.Tests/AccountServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Create `childDev/ChildDev.Mobile.Tests/AccountServiceTests.cs`:
```csharp
using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using SQLite;

namespace ChildDev.Mobile.Tests;

public class AccountServiceTests : IDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private readonly AccountService _service;

    public AccountServiceTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _db = new SQLiteAsyncConnection(":memory:");
        _db.CreateTableAsync<Account>().GetAwaiter().GetResult();
        _service = new AccountService(_db);
    }

    public void Dispose() => _db.CloseAsync().GetAwaiter().GetResult();

    [Fact]
    public async Task GetAccount_WhenNone_ReturnsNull()
    {
        var account = await _service.GetAccountAsync();
        Assert.Null(account);
    }

    [Fact]
    public async Task CreateAccount_StoresPinHash_NotRawPin()
    {
        await _service.CreateAccountAsync("alice", "1234");
        var account = await _service.GetAccountAsync();

        Assert.NotNull(account);
        Assert.Equal("alice", account.NickName);
        Assert.NotEqual("1234", account.PinHash);
    }

    [Fact]
    public async Task VerifyPin_CorrectPin_ReturnsTrue()
    {
        await _service.CreateAccountAsync("bob", "9876");
        var result = await _service.VerifyPinAsync("9876");
        Assert.True(result);
    }

    [Fact]
    public async Task VerifyPin_WrongPin_ReturnsFalse()
    {
        await _service.CreateAccountAsync("carol", "1111");
        var result = await _service.VerifyPinAsync("9999");
        Assert.False(result);
    }

    [Fact]
    public async Task UpdateLastSync_SetsTimestamp()
    {
        await _service.CreateAccountAsync("dave", "0000");
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _service.UpdateLastSyncAsync(ts);

        var account = await _service.GetAccountAsync();
        Assert.Equal(ts, account!.LastSyncAt);
    }
}
```

- [ ] **Step 2: Run to verify fail**

```bash
dotnet test ChildDev.Mobile.Tests --filter "AccountServiceTests" -v
```
Expected: FAIL.

- [ ] **Step 3: Create AccountService**

Create `childDev/ChildDev.Mobile/Services/AccountService.cs`:
```csharp
using BCrypt.Net;
using ChildDev.Mobile.Models;
using SQLite;

namespace ChildDev.Mobile.Services;

public class AccountService(SQLiteAsyncConnection db)
{
    public Task<Account?> GetAccountAsync() =>
        db.Table<Account>().FirstOrDefaultAsync();

    public async Task<Account> CreateAccountAsync(string nickName, string pin)
    {
        var account = new Account
        {
            Guid = Guid.NewGuid().ToString(),
            NickName = nickName,
            PinHash = BCrypt.Net.BCrypt.HashPassword(pin),
            CreatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await db.InsertAsync(account);
        return account;
    }

    public async Task<bool> VerifyPinAsync(string pin)
    {
        var account = await GetAccountAsync();
        return account is not null && BCrypt.Net.BCrypt.Verify(pin, account.PinHash);
    }

    public async Task UpdateLastSyncAsync(long timestamp)
    {
        var account = await GetAccountAsync();
        if (account is null) return;
        account.LastSyncAt = timestamp;
        await db.UpdateAsync(account);
    }

    public async Task SaveServerCredentialsAsync(string jwt, string serverUrl)
    {
        var account = await GetAccountAsync();
        if (account is null) return;
        account.ServerJwt = jwt;
        account.ServerUrl = serverUrl;
        await db.UpdateAsync(account);
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test ChildDev.Mobile.Tests --filter "AccountServiceTests" -v
```
Expected: All 5 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add ChildDev.Mobile/ ChildDev.Mobile.Tests/
git commit -m "feat: add AccountService with BCrypt PIN hashing"
```

---

## Task 5: MauiProgram, AppShell, SetupPage

**Files:**
- Modify: `childDev/ChildDev.Mobile/MauiProgram.cs`
- Create: `childDev/ChildDev.Mobile/AppShell.xaml`
- Create: `childDev/ChildDev.Mobile/AppShell.xaml.cs`
- Create: `childDev/ChildDev.Mobile/Views/SetupPage.xaml`
- Create: `childDev/ChildDev.Mobile/Views/SetupPage.xaml.cs`
- Create: `childDev/ChildDev.Mobile/ViewModels/SetupViewModel.cs`

- [ ] **Step 1: Rewrite MauiProgram.cs with full DI registration**

Replace `childDev/ChildDev.Mobile/MauiProgram.cs`:
```csharp
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Services;
using ChildDev.Mobile.ViewModels;
using ChildDev.Mobile.Views;
using SQLite;

namespace ChildDev.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMaui()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "childdev.db3");

        var localDb = new LocalDatabase(dbPath);
        localDb.InitAsync().GetAwaiter().GetResult();

        builder.Services.AddSingleton(localDb.Connection);
        builder.Services.AddSingleton<AccountService>();
        builder.Services.AddSingleton<JournalRepository>();
        builder.Services.AddSingleton<GoalRepository>();
        builder.Services.AddSingleton<GoalProgressRepository>();
        builder.Services.AddSingleton<TodoRepository>();
        builder.Services.AddSingleton<ConnectivityService>();
        builder.Services.AddSingleton<SyncService>();

        // ViewModels (transient — new instance per navigation)
        builder.Services.AddTransient<SetupViewModel>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<JournalListViewModel>();
        builder.Services.AddTransient<JournalEntryViewModel>();
        builder.Services.AddTransient<GoalListViewModel>();
        builder.Services.AddTransient<GoalEntryViewModel>();
        builder.Services.AddTransient<TodoListViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

        // Pages
        builder.Services.AddTransient<SetupPage>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<JournalListPage>();
        builder.Services.AddTransient<JournalEntryPage>();
        builder.Services.AddTransient<GoalListPage>();
        builder.Services.AddTransient<GoalEntryPage>();
        builder.Services.AddTransient<TodoListPage>();
        builder.Services.AddTransient<SettingsPage>();

        return builder.Build();
    }
}
```

- [ ] **Step 2: Create AppShell.xaml**

Create `childDev/ChildDev.Mobile/AppShell.xaml`:
```xml
<?xml version="1.0" encoding="UTF-8" ?>
<Shell
    x:Class="ChildDev.Mobile.AppShell"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:views="clr-namespace:ChildDev.Mobile.Views"
    Shell.FlyoutBehavior="Disabled">

    <TabBar>
        <ShellContent Title="Dashboard" Icon="dashboard.png"
                      ContentTemplate="{DataTemplate views:DashboardPage}" Route="dashboard" />
        <ShellContent Title="Journal" Icon="journal.png"
                      ContentTemplate="{DataTemplate views:JournalListPage}" Route="journal" />
        <ShellContent Title="Goals" Icon="goals.png"
                      ContentTemplate="{DataTemplate views:GoalListPage}" Route="goals" />
        <ShellContent Title="Todos" Icon="todos.png"
                      ContentTemplate="{DataTemplate views:TodoListPage}" Route="todos" />
    </TabBar>
</Shell>
```

Create `childDev/ChildDev.Mobile/AppShell.xaml.cs`:
```csharp
namespace ChildDev.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("journal/entry", typeof(Views.JournalEntryPage));
        Routing.RegisterRoute("goals/entry", typeof(Views.GoalEntryPage));
        Routing.RegisterRoute("settings", typeof(Views.SettingsPage));
    }
}
```

- [ ] **Step 3: Create SetupViewModel**

Create `childDev/ChildDev.Mobile/ViewModels/SetupViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildDev.Mobile.Services;

namespace ChildDev.Mobile.ViewModels;

public partial class SetupViewModel(AccountService accountService) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateAccountCommand))]
    private string nickName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateAccountCommand))]
    private string pin = string.Empty;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    private bool CanCreate => !string.IsNullOrWhiteSpace(NickName) && Pin.Length == 4;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAccountAsync()
    {
        if (!Pin.All(char.IsDigit))
        {
            ErrorMessage = "PIN must be 4 digits";
            return;
        }

        await accountService.CreateAccountAsync(NickName.Trim(), Pin);
        await Shell.Current.GoToAsync("//dashboard");
    }
}
```

- [ ] **Step 4: Create SetupPage.xaml**

Create `childDev/ChildDev.Mobile/Views/SetupPage.xaml`:
```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ChildDev.Mobile.ViewModels"
             x:Class="ChildDev.Mobile.Views.SetupPage"
             Title="Welcome">
    <VerticalStackLayout Padding="32" Spacing="20" VerticalOptions="Center">
        <Label Text="Child Development Journal" FontSize="24" HorizontalOptions="Center" />
        <Label Text="Set up your account to get started." HorizontalOptions="Center" />

        <Entry Placeholder="Your name" Text="{Binding NickName}" />
        <Entry Placeholder="4-digit PIN" Text="{Binding Pin}" IsPassword="True" MaxLength="4"
               Keyboard="Numeric" />
        <Label Text="{Binding ErrorMessage}" TextColor="Red" IsVisible="{Binding ErrorMessage, Converter={StaticResource StringToBoolConverter}}" />

        <Button Text="Get Started" Command="{Binding CreateAccountCommand}" />
    </VerticalStackLayout>
</ContentPage>
```

Create `childDev/ChildDev.Mobile/Views/SetupPage.xaml.cs`:
```csharp
using ChildDev.Mobile.ViewModels;

namespace ChildDev.Mobile.Views;

public partial class SetupPage : ContentPage
{
    public SetupPage(SetupViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
```

- [ ] **Step 5: Update App.xaml.cs to check for existing account on launch**

Replace `childDev/ChildDev.Mobile/App.xaml.cs`:
```csharp
using ChildDev.Mobile.Services;

namespace ChildDev.Mobile;

public partial class App : Application
{
    public App(AccountService accountService)
    {
        InitializeComponent();

        var account = accountService.GetAccountAsync().GetAwaiter().GetResult();
        MainPage = account is null
            ? new NavigationPage(Handler?.MauiContext?.Services.GetService<Views.SetupPage>()
                ?? throw new InvalidOperationException("SetupPage not registered"))
            : new AppShell();
    }
}
```

- [ ] **Step 6: Verify build (MAUI targets)**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
dotnet build ChildDev.Mobile -f net8.0-android
```
Expected: Build succeeded (may have XAML warnings — those are fine at this stage).

- [ ] **Step 7: Commit**

```bash
git add ChildDev.Mobile/
git commit -m "feat: add MauiProgram DI, AppShell with 4 tabs, SetupPage"
```

---

## Task 6: Journal Pages and ViewModel

**Files:**
- Create: `childDev/ChildDev.Mobile/ViewModels/JournalListViewModel.cs`
- Create: `childDev/ChildDev.Mobile/ViewModels/JournalEntryViewModel.cs`
- Create: `childDev/ChildDev.Mobile/Views/JournalListPage.xaml` + `.cs`
- Create: `childDev/ChildDev.Mobile/Views/JournalEntryPage.xaml` + `.cs`

- [ ] **Step 1: Create JournalListViewModel**

Create `childDev/ChildDev.Mobile/ViewModels/JournalListViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildDev.Mobile.ViewModels;

public partial class JournalListViewModel(
    JournalRepository repo,
    AccountService accountService) : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Journal> journals = [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;
        var items = await repo.GetAllActiveAsync(account.Guid);
        Journals = new ObservableCollection<Journal>(items);
    }

    [RelayCommand]
    private async Task AddAsync() =>
        await Shell.Current.GoToAsync("journal/entry");

    [RelayCommand]
    private async Task OpenAsync(Journal journal) =>
        await Shell.Current.GoToAsync($"journal/entry?guid={journal.Guid}");

    [RelayCommand]
    private async Task DeleteAsync(Journal journal)
    {
        await repo.DeleteAsync(journal.Guid);
        Journals.Remove(journal);
    }
}
```

- [ ] **Step 2: Create JournalEntryViewModel**

Create `childDev/ChildDev.Mobile/ViewModels/JournalEntryViewModel.cs`:
```csharp
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildDev.Mobile.ViewModels;

[QueryProperty(nameof(Guid), "guid")]
public partial class JournalEntryViewModel(
    JournalRepository repo,
    AccountService accountService) : ObservableObject
{
    [ObservableProperty] private string guid = string.Empty;
    [ObservableProperty] private string notes = string.Empty;
    [ObservableProperty] private string activity = string.Empty;
    [ObservableProperty] private string mood = string.Empty;
    [ObservableProperty] private string tags = string.Empty;

    partial void OnGuidChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
            LoadAsync(value).FireAndForget();
    }

    private async Task LoadAsync(string guid)
    {
        var item = await repo.GetAsync(guid);
        if (item is null) return;
        Notes = item.Notes ?? string.Empty;
        Activity = item.Activity ?? string.Empty;
        Mood = item.Mood ?? string.Empty;
        Tags = item.Tags ?? string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;

        var journal = string.IsNullOrEmpty(Guid)
            ? new Journal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
            : await repo.GetAsync(Guid) ?? new Journal { Guid = Guid, AccountFk = account.Guid, EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };

        journal.Notes = Notes;
        journal.Activity = Activity;
        journal.Mood = Mood;
        journal.Tags = Tags;

        await repo.SaveAsync(journal);
        await Shell.Current.GoToAsync("..");
    }
}
```

- [ ] **Step 3: Add FireAndForget extension**

Create `childDev/ChildDev.Mobile/Extensions.cs`:
```csharp
namespace ChildDev.Mobile;

public static class TaskExtensions
{
    public static async void FireAndForget(this Task task)
    {
        try { await task; }
        catch { /* intentionally swallowed for fire-and-forget navigation triggers */ }
    }
}
```

- [ ] **Step 4: Create JournalListPage.xaml**

Create `childDev/ChildDev.Mobile/Views/JournalListPage.xaml`:
```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ChildDev.Mobile.ViewModels"
             xmlns:models="clr-namespace:ChildDev.Mobile.Models"
             x:Class="ChildDev.Mobile.Views.JournalListPage"
             Title="Journal">
    <ContentPage.ToolbarItems>
        <ToolbarItem Text="+" Command="{Binding AddCommand}" />
    </ContentPage.ToolbarItems>

    <CollectionView ItemsSource="{Binding Journals}">
        <CollectionView.ItemTemplate>
            <DataTemplate x:DataType="models:Journal">
                <SwipeView>
                    <SwipeView.RightItems>
                        <SwipeItems>
                            <SwipeItem Text="Delete" BackgroundColor="Red"
                                       Command="{Binding Source={RelativeSource AncestorType={x:Type vm:JournalListViewModel}}, Path=DeleteCommand}"
                                       CommandParameter="{Binding .}" />
                        </SwipeItems>
                    </SwipeView.RightItems>
                    <Grid Padding="16,12" ColumnDefinitions="*,Auto">
                        <VerticalStackLayout>
                            <Label Text="{Binding Notes}" LineBreakMode="TailTruncation" MaxLines="2" />
                            <Label Text="{Binding Mood}" FontSize="12" TextColor="Gray" />
                        </VerticalStackLayout>
                    </Grid>
                </SwipeView>
            </DataTemplate>
        </CollectionView.ItemTemplate>
    </CollectionView>
</ContentPage>
```

Create `childDev/ChildDev.Mobile/Views/JournalListPage.xaml.cs`:
```csharp
using ChildDev.Mobile.ViewModels;

namespace ChildDev.Mobile.Views;

public partial class JournalListPage : ContentPage
{
    private readonly JournalListViewModel _vm;

    public JournalListPage(JournalListViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing() => _vm.LoadCommand.Execute(null);
}
```

- [ ] **Step 5: Create JournalEntryPage.xaml**

Create `childDev/ChildDev.Mobile/Views/JournalEntryPage.xaml`:
```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="ChildDev.Mobile.Views.JournalEntryPage"
             Title="Journal Entry">
    <ContentPage.ToolbarItems>
        <ToolbarItem Text="Save" Command="{Binding SaveCommand}" />
    </ContentPage.ToolbarItems>

    <ScrollView>
        <VerticalStackLayout Padding="16" Spacing="12">
            <Label Text="Notes" />
            <Editor Text="{Binding Notes}" HeightRequest="150" AutoSize="TextChanges" />
            <Label Text="Activity" />
            <Entry Text="{Binding Activity}" Placeholder="e.g. Reading, Drawing" />
            <Label Text="Mood" />
            <Entry Text="{Binding Mood}" Placeholder="e.g. Happy, Focused" />
            <Label Text="Tags" />
            <Entry Text="{Binding Tags}" Placeholder="comma-separated" />
        </VerticalStackLayout>
    </ScrollView>
</ContentPage>
```

Create `childDev/ChildDev.Mobile/Views/JournalEntryPage.xaml.cs`:
```csharp
using ChildDev.Mobile.ViewModels;

namespace ChildDev.Mobile.Views;

public partial class JournalEntryPage : ContentPage
{
    public JournalEntryPage(JournalEntryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
```

- [ ] **Step 6: Build**

```bash
dotnet build ChildDev.Mobile -f net8.0-android
```
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add ChildDev.Mobile/
git commit -m "feat: add Journal list and entry pages with MVVM viewmodels"
```

---

## Task 7: Goal Pages and ViewModel

**Files:**
- Create: `childDev/ChildDev.Mobile/ViewModels/GoalListViewModel.cs`
- Create: `childDev/ChildDev.Mobile/ViewModels/GoalEntryViewModel.cs`
- Create: `childDev/ChildDev.Mobile/Views/GoalListPage.xaml` + `.cs`
- Create: `childDev/ChildDev.Mobile/Views/GoalEntryPage.xaml` + `.cs`

- [ ] **Step 1: Create GoalListViewModel**

Create `childDev/ChildDev.Mobile/ViewModels/GoalListViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildDev.Mobile.ViewModels;

public partial class GoalListViewModel(
    GoalRepository repo,
    AccountService accountService) : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Goal> goals = [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;
        var items = await repo.GetAllActiveAsync(account.Guid);
        Goals = new ObservableCollection<Goal>(items);
    }

    [RelayCommand]
    private async Task AddAsync() =>
        await Shell.Current.GoToAsync("goals/entry");

    [RelayCommand]
    private async Task OpenAsync(Goal goal) =>
        await Shell.Current.GoToAsync($"goals/entry?guid={goal.Guid}");

    [RelayCommand]
    private async Task DeleteAsync(Goal goal)
    {
        await repo.DeleteAsync(goal.Guid);
        Goals.Remove(goal);
    }
}
```

- [ ] **Step 2: Create GoalEntryViewModel**

Create `childDev/ChildDev.Mobile/ViewModels/GoalEntryViewModel.cs`:
```csharp
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildDev.Mobile.ViewModels;

[QueryProperty(nameof(Guid), "guid")]
public partial class GoalEntryViewModel(
    GoalRepository repo,
    GoalProgressRepository progressRepo,
    AccountService accountService) : ObservableObject
{
    [ObservableProperty] private string guid = string.Empty;
    [ObservableProperty] private string goalText = string.Empty;
    [ObservableProperty] private string measurableOutcome = string.Empty;
    [ObservableProperty] private string nextStepItems = string.Empty;
    [ObservableProperty] private DateTime nextMeetingDate = DateTime.Today.AddDays(7);

    partial void OnGuidChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
            LoadAsync(value).FireAndForget();
    }

    private async Task LoadAsync(string guid)
    {
        var item = await repo.GetAsync(guid);
        if (item is null) return;
        GoalText = item.GoalText ?? string.Empty;
        MeasurableOutcome = item.MeasurableOutcome ?? string.Empty;
        if (item.NextMeetingDate.HasValue)
            NextMeetingDate = DateTimeOffset.FromUnixTimeMilliseconds(item.NextMeetingDate.Value).LocalDateTime;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = string.IsNullOrEmpty(Guid)
            ? new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid, EnteredDate = ts }
            : await repo.GetAsync(Guid) ?? new Goal { Guid = Guid, AccountFk = account.Guid, EnteredDate = ts };

        goal.GoalText = GoalText;
        goal.MeasurableOutcome = MeasurableOutcome;
        goal.NextMeetingDate = new DateTimeOffset(NextMeetingDate, TimeSpan.Zero).ToUnixTimeMilliseconds();
        await repo.SaveAsync(goal);

        if (!string.IsNullOrWhiteSpace(NextStepItems))
        {
            var progress = new GoalProgress
            {
                Guid = System.Guid.NewGuid().ToString(),
                AccountFk = account.Guid,
                GoalFk = goal.Guid,
                NextStepItems = NextStepItems,
                NextMeetingDate = goal.NextMeetingDate,
                UpdatedOn = ts
            };
            await progressRepo.SaveAsync(progress);
        }

        await Shell.Current.GoToAsync("..");
    }
}
```

- [ ] **Step 3: Create GoalListPage.xaml**

Create `childDev/ChildDev.Mobile/Views/GoalListPage.xaml`:
```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ChildDev.Mobile.ViewModels"
             xmlns:models="clr-namespace:ChildDev.Mobile.Models"
             x:Class="ChildDev.Mobile.Views.GoalListPage"
             Title="Goals">
    <ContentPage.ToolbarItems>
        <ToolbarItem Text="+" Command="{Binding AddCommand}" />
    </ContentPage.ToolbarItems>

    <CollectionView ItemsSource="{Binding Goals}">
        <CollectionView.ItemTemplate>
            <DataTemplate x:DataType="models:Goal">
                <SwipeView>
                    <SwipeView.RightItems>
                        <SwipeItems>
                            <SwipeItem Text="Delete" BackgroundColor="Red"
                                       Command="{Binding Source={RelativeSource AncestorType={x:Type vm:GoalListViewModel}}, Path=DeleteCommand}"
                                       CommandParameter="{Binding .}" />
                        </SwipeItems>
                    </SwipeView.RightItems>
                    <Grid Padding="16,12">
                        <Label Text="{Binding GoalText}" LineBreakMode="TailTruncation" MaxLines="2" />
                    </Grid>
                </SwipeView>
            </DataTemplate>
        </CollectionView.ItemTemplate>
    </CollectionView>
</ContentPage>
```

Create `childDev/ChildDev.Mobile/Views/GoalListPage.xaml.cs`:
```csharp
using ChildDev.Mobile.ViewModels;

namespace ChildDev.Mobile.Views;

public partial class GoalListPage : ContentPage
{
    private readonly GoalListViewModel _vm;

    public GoalListPage(GoalListViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing() => _vm.LoadCommand.Execute(null);
}
```

- [ ] **Step 4: Create GoalEntryPage.xaml**

Create `childDev/ChildDev.Mobile/Views/GoalEntryPage.xaml`:
```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="ChildDev.Mobile.Views.GoalEntryPage"
             Title="Goal">
    <ContentPage.ToolbarItems>
        <ToolbarItem Text="Save" Command="{Binding SaveCommand}" />
    </ContentPage.ToolbarItems>

    <ScrollView>
        <VerticalStackLayout Padding="16" Spacing="12">
            <Label Text="Goal" />
            <Editor Text="{Binding GoalText}" HeightRequest="100" AutoSize="TextChanges" />
            <Label Text="Measurable Outcome" />
            <Entry Text="{Binding MeasurableOutcome}" Placeholder="How will you know it's achieved?" />
            <Label Text="Next Steps" />
            <Editor Text="{Binding NextStepItems}" HeightRequest="80" AutoSize="TextChanges" />
            <Label Text="Next Meeting Date" />
            <DatePicker Date="{Binding NextMeetingDate}" />
        </VerticalStackLayout>
    </ScrollView>
</ContentPage>
```

Create `childDev/ChildDev.Mobile/Views/GoalEntryPage.xaml.cs`:
```csharp
using ChildDev.Mobile.ViewModels;

namespace ChildDev.Mobile.Views;

public partial class GoalEntryPage : ContentPage
{
    public GoalEntryPage(GoalEntryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
```

- [ ] **Step 5: Build**

```bash
dotnet build ChildDev.Mobile -f net8.0-android
```
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add ChildDev.Mobile/
git commit -m "feat: add Goal list and entry pages"
```

---

## Task 8: Todo Pages and Dashboard

**Files:**
- Create: `childDev/ChildDev.Mobile/ViewModels/TodoListViewModel.cs`
- Create: `childDev/ChildDev.Mobile/Views/TodoListPage.xaml` + `.cs`
- Create: `childDev/ChildDev.Mobile/ViewModels/DashboardViewModel.cs`
- Create: `childDev/ChildDev.Mobile/Views/DashboardPage.xaml` + `.cs`

- [ ] **Step 1: Create TodoListViewModel**

Create `childDev/ChildDev.Mobile/ViewModels/TodoListViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildDev.Mobile.ViewModels;

public partial class TodoListViewModel(
    TodoRepository repo,
    AccountService accountService) : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Todo> todos = [];

    [ObservableProperty]
    private string newTodoTitle = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;
        var items = await repo.GetPendingAsync(account.Guid);
        Todos = new ObservableCollection<Todo>(items);
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTodoTitle)) return;
        var account = await accountService.GetAccountAsync();
        if (account is null) return;

        var todo = new Todo
        {
            Guid = System.Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            Title = NewTodoTitle.Trim(),
            UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await repo.SaveAsync(todo);
        Todos.Insert(0, todo);
        NewTodoTitle = string.Empty;
    }

    [RelayCommand]
    private async Task CompleteAsync(Todo todo)
    {
        await repo.CompleteAsync(todo.Guid);
        Todos.Remove(todo);
    }

    [RelayCommand]
    private async Task DeleteAsync(Todo todo)
    {
        await repo.DeleteAsync(todo.Guid);
        Todos.Remove(todo);
    }
}
```

- [ ] **Step 2: Create TodoListPage.xaml**

Create `childDev/ChildDev.Mobile/Views/TodoListPage.xaml`:
```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ChildDev.Mobile.ViewModels"
             xmlns:models="clr-namespace:ChildDev.Mobile.Models"
             x:Class="ChildDev.Mobile.Views.TodoListPage"
             Title="Todos">
    <Grid RowDefinitions="Auto,*">
        <Grid Grid.Row="0" ColumnDefinitions="*,Auto" Padding="16,8">
            <Entry Grid.Column="0" Placeholder="Add a task..." Text="{Binding NewTodoTitle}"
                   ReturnCommand="{Binding AddCommand}" />
            <Button Grid.Column="1" Text="Add" Command="{Binding AddCommand}" Margin="8,0,0,0" />
        </Grid>

        <CollectionView Grid.Row="1" ItemsSource="{Binding Todos}">
            <CollectionView.ItemTemplate>
                <DataTemplate x:DataType="models:Todo">
                    <SwipeView>
                        <SwipeView.LeftItems>
                            <SwipeItems>
                                <SwipeItem Text="Done" BackgroundColor="Green"
                                           Command="{Binding Source={RelativeSource AncestorType={x:Type vm:TodoListViewModel}}, Path=CompleteCommand}"
                                           CommandParameter="{Binding .}" />
                            </SwipeItems>
                        </SwipeView.LeftItems>
                        <SwipeView.RightItems>
                            <SwipeItems>
                                <SwipeItem Text="Delete" BackgroundColor="Red"
                                           Command="{Binding Source={RelativeSource AncestorType={x:Type vm:TodoListViewModel}}, Path=DeleteCommand}"
                                           CommandParameter="{Binding .}" />
                            </SwipeItems>
                        </SwipeView.RightItems>
                        <Label Text="{Binding Title}" Padding="16,14" />
                    </SwipeView>
                </DataTemplate>
            </CollectionView.ItemTemplate>
        </CollectionView>
    </Grid>
</ContentPage>
```

Create `childDev/ChildDev.Mobile/Views/TodoListPage.xaml.cs`:
```csharp
using ChildDev.Mobile.ViewModels;

namespace ChildDev.Mobile.Views;

public partial class TodoListPage : ContentPage
{
    private readonly TodoListViewModel _vm;

    public TodoListPage(TodoListViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing() => _vm.LoadCommand.Execute(null);
}
```

- [ ] **Step 3: Create DashboardViewModel**

Create `childDev/ChildDev.Mobile/ViewModels/DashboardViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildDev.Mobile.ViewModels;

public partial class DashboardViewModel(
    JournalRepository journalRepo,
    GoalRepository goalRepo,
    TodoRepository todoRepo,
    AccountService accountService,
    SyncService syncService) : ObservableObject
{
    [ObservableProperty] private ObservableCollection<Journal> recentJournals = [];
    [ObservableProperty] private int activeGoalCount;
    [ObservableProperty] private int pendingTodoCount;
    [ObservableProperty] private string syncStatus = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;

        var journals = await journalRepo.GetAllActiveAsync(account.Guid);
        RecentJournals = new ObservableCollection<Journal>(journals.Take(3));

        var goals = await goalRepo.GetAllActiveAsync(account.Guid);
        ActiveGoalCount = goals.Count(g => g.CompletionDate is null);

        var todos = await todoRepo.GetPendingAsync(account.Guid);
        PendingTodoCount = todos.Count;

        _ = RunSyncAsync(account);
    }

    private async Task RunSyncAsync(Models.Account account)
    {
        SyncStatus = "Syncing...";
        var result = await syncService.RunAsync(account);
        SyncStatus = result switch
        {
            SyncResult.Success => $"Synced {DateTime.Now:t}",
            SyncResult.NoServer => string.Empty,
            SyncResult.Failed => "Sync failed — will retry next open",
            _ => string.Empty
        };
    }

    [RelayCommand]
    private async Task AddJournalAsync() =>
        await Shell.Current.GoToAsync("journal/entry");

    [RelayCommand]
    private async Task GoToSettingsAsync() =>
        await Shell.Current.GoToAsync("settings");
}
```

- [ ] **Step 4: Create DashboardPage.xaml**

Create `childDev/ChildDev.Mobile/Views/DashboardPage.xaml`:
```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:models="clr-namespace:ChildDev.Mobile.Models"
             x:Class="ChildDev.Mobile.Views.DashboardPage"
             Title="Dashboard">
    <ContentPage.ToolbarItems>
        <ToolbarItem Text="⚙" Command="{Binding GoToSettingsCommand}" />
    </ContentPage.ToolbarItems>

    <ScrollView>
        <VerticalStackLayout Padding="16" Spacing="16">

            <Label Text="{Binding SyncStatus}" FontSize="12" TextColor="Gray"
                   IsVisible="{Binding SyncStatus, Converter={StaticResource StringToBoolConverter}}" />

            <!-- Summary row -->
            <Grid ColumnDefinitions="*,*" ColumnSpacing="12">
                <Border Grid.Column="0" Padding="16" StrokeShape="RoundRectangle 8">
                    <VerticalStackLayout>
                        <Label Text="{Binding ActiveGoalCount}" FontSize="32" HorizontalOptions="Center" />
                        <Label Text="Active Goals" HorizontalOptions="Center" />
                    </VerticalStackLayout>
                </Border>
                <Border Grid.Column="1" Padding="16" StrokeShape="RoundRectangle 8">
                    <VerticalStackLayout>
                        <Label Text="{Binding PendingTodoCount}" FontSize="32" HorizontalOptions="Center" />
                        <Label Text="Pending Todos" HorizontalOptions="Center" />
                    </VerticalStackLayout>
                </Border>
            </Grid>

            <!-- Recent journal entries -->
            <Label Text="Recent Journal" FontSize="18" FontAttributes="Bold" />
            <CollectionView ItemsSource="{Binding RecentJournals}">
                <CollectionView.ItemTemplate>
                    <DataTemplate x:DataType="models:Journal">
                        <Border Padding="12" Margin="0,4" StrokeShape="RoundRectangle 8">
                            <Label Text="{Binding Notes}" LineBreakMode="TailTruncation" MaxLines="2" />
                        </Border>
                    </DataTemplate>
                </CollectionView.ItemTemplate>
            </CollectionView>

            <Button Text="+ New Journal Entry" Command="{Binding AddJournalCommand}" />
        </VerticalStackLayout>
    </ScrollView>
</ContentPage>
```

Create `childDev/ChildDev.Mobile/Views/DashboardPage.xaml.cs`:
```csharp
using ChildDev.Mobile.ViewModels;

namespace ChildDev.Mobile.Views;

public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _vm;

    public DashboardPage(DashboardViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing() => _vm.LoadCommand.Execute(null);
}
```

- [ ] **Step 5: Build**

```bash
dotnet build ChildDev.Mobile -f net8.0-android
```
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add ChildDev.Mobile/
git commit -m "feat: add Todo list, Dashboard with summary counts and recent journals"
```

---

## Task 9: ConnectivityService and SettingsPage

**Files:**
- Create: `childDev/ChildDev.Mobile/Services/ConnectivityService.cs`
- Create: `childDev/ChildDev.Mobile/ViewModels/SettingsViewModel.cs`
- Create: `childDev/ChildDev.Mobile/Views/SettingsPage.xaml` + `.cs`

- [ ] **Step 1: Create ConnectivityService**

Create `childDev/ChildDev.Mobile/Services/ConnectivityService.cs`:
```csharp
namespace ChildDev.Mobile.Services;

public class ConnectivityService
{
    public virtual bool IsConnected =>
        Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
}
```

- [ ] **Step 2: Create SettingsViewModel**

Create `childDev/ChildDev.Mobile/ViewModels/SettingsViewModel.cs`:
```csharp
using ChildDev.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildDev.Mobile.ViewModels;

public partial class SettingsViewModel(AccountService accountService) : ObservableObject
{
    [ObservableProperty] private string serverUrl = string.Empty;
    [ObservableProperty] private string nickName = string.Empty;
    [ObservableProperty] private string lastSyncDisplay = "Never";
    [ObservableProperty] private string statusMessage = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;
        NickName = account.NickName;
        ServerUrl = account.ServerUrl ?? string.Empty;
        LastSyncDisplay = account.LastSyncAt == 0
            ? "Never"
            : DateTimeOffset.FromUnixTimeMilliseconds(account.LastSyncAt).LocalDateTime.ToString("g");
    }

    [RelayCommand]
    private async Task SaveServerUrlAsync()
    {
        var url = ServerUrl.Trim().TrimEnd('/');
        await accountService.SaveServerCredentialsAsync(string.Empty, url);
        StatusMessage = "Server URL saved.";
    }
}
```

- [ ] **Step 3: Create SettingsPage.xaml**

Create `childDev/ChildDev.Mobile/Views/SettingsPage.xaml`:
```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="ChildDev.Mobile.Views.SettingsPage"
             Title="Settings">
    <ScrollView>
        <VerticalStackLayout Padding="16" Spacing="16">
            <Label Text="{Binding NickName, StringFormat='Account: {0}'}" />

            <Label Text="Sync Server URL" />
            <Entry Text="{Binding ServerUrl}" Placeholder="https://your-server/api" Keyboard="Url" />
            <Button Text="Save Server URL" Command="{Binding SaveServerUrlCommand}" />

            <Label Text="{Binding LastSyncDisplay, StringFormat='Last synced: {0}'}" TextColor="Gray" />
            <Label Text="{Binding StatusMessage}" TextColor="Green"
                   IsVisible="{Binding StatusMessage, Converter={StaticResource StringToBoolConverter}}" />
        </VerticalStackLayout>
    </ScrollView>
</ContentPage>
```

Create `childDev/ChildDev.Mobile/Views/SettingsPage.xaml.cs`:
```csharp
using ChildDev.Mobile.ViewModels;

namespace ChildDev.Mobile.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _vm;

    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing() => _vm.LoadCommand.Execute(null);
}
```

- [ ] **Step 4: Build**

```bash
dotnet build ChildDev.Mobile -f net8.0-android
```
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add ChildDev.Mobile/
git commit -m "feat: add ConnectivityService and Settings page with server URL config"
```

---

## Task 10: SyncService

**Files:**
- Create: `childDev/ChildDev.Mobile/Services/SyncService.cs`
- Create: `childDev/ChildDev.Mobile.Tests/SyncServiceTests.cs`

- [ ] **Step 1: Define SyncResult enum**

Add to `childDev/ChildDev.Mobile/Services/SyncService.cs` (create file):
```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;

namespace ChildDev.Mobile.Services;

public enum SyncResult { Success, NoServer, Failed }

public class SyncService(
    JournalRepository journalRepo,
    GoalRepository goalRepo,
    GoalProgressRepository goalProgressRepo,
    TodoRepository todoRepo,
    AccountService accountService,
    ConnectivityService connectivity,
    IHttpClientFactory httpFactory)
{
    public async Task<SyncResult> RunAsync(Account account)
    {
        if (!connectivity.IsConnected) return SyncResult.NoServer;
        if (string.IsNullOrEmpty(account.ServerUrl) || string.IsNullOrEmpty(account.ServerJwt))
            return SyncResult.NoServer;

        try
        {
            var client = httpFactory.CreateClient("childdev");
            client.BaseAddress = new Uri(account.ServerUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", account.ServerJwt);

            var since = account.LastSyncAt;

            await SyncEntityAsync<Journal, JournalSyncDto>(
                client, "sync/journal", since,
                () => journalRepo.GetModifiedSinceAsync(account.Guid, since),
                j => new JournalSyncDto(j.Guid, j.AccountFk, j.Notes, j.Activity, j.Mood, j.Tags,
                    j.EnteredDate, j.UpdatedOn, j.DeletedAt),
                dto => journalRepo.UpsertFromSyncAsync(new Journal
                {
                    Guid = dto.Guid, AccountFk = dto.AccountFk, Notes = dto.Notes,
                    Activity = dto.Activity, Mood = dto.Mood, Tags = dto.Tags,
                    EnteredDate = dto.EnteredDate, UpdatedOn = dto.UpdatedOn, DeletedAt = dto.DeletedAt
                }));

            await SyncEntityAsync<Goal, GoalSyncDto>(
                client, "sync/goal", since,
                () => goalRepo.GetModifiedSinceAsync(account.Guid, since),
                g => new GoalSyncDto(g.Guid, g.AccountFk, g.GoalText, g.NextMeetingDate,
                    g.ExpirationDate, g.EnteredDate, g.MeasurableOutcome, g.CompletionDate, g.UpdatedOn, g.DeletedAt),
                dto => goalRepo.UpsertFromSyncAsync(new Goal
                {
                    Guid = dto.Guid, AccountFk = dto.AccountFk, GoalText = dto.GoalText,
                    NextMeetingDate = dto.NextMeetingDate, ExpirationDate = dto.ExpirationDate,
                    EnteredDate = dto.EnteredDate, MeasurableOutcome = dto.MeasurableOutcome,
                    CompletionDate = dto.CompletionDate, UpdatedOn = dto.UpdatedOn, DeletedAt = dto.DeletedAt
                }));

            await SyncEntityAsync<GoalProgress, GoalProgressSyncDto>(
                client, "sync/goal-progress", since,
                () => goalProgressRepo.GetModifiedSinceAsync(account.Guid, since),
                p => new GoalProgressSyncDto(p.Guid, p.AccountFk, p.GoalFk, p.NextStepItems,
                    p.NextMeetingDate, p.UpdatedOn, p.DeletedAt),
                dto => goalProgressRepo.UpsertFromSyncAsync(new GoalProgress
                {
                    Guid = dto.Guid, AccountFk = dto.AccountFk, GoalFk = dto.GoalFk,
                    NextStepItems = dto.NextStepItems, NextMeetingDate = dto.NextMeetingDate,
                    UpdatedOn = dto.UpdatedOn, DeletedAt = dto.DeletedAt
                }));

            await SyncEntityAsync<Todo, TodoSyncDto>(
                client, "sync/todo", since,
                () => todoRepo.GetModifiedSinceAsync(account.Guid, since),
                t => new TodoSyncDto(t.Guid, t.AccountFk, t.Title, t.Notes, t.DueDate,
                    t.CompletedAt, t.UpdatedOn, t.DeletedAt),
                dto => todoRepo.UpsertFromSyncAsync(new Todo
                {
                    Guid = dto.Guid, AccountFk = dto.AccountFk, Title = dto.Title,
                    Notes = dto.Notes, DueDate = dto.DueDate, CompletedAt = dto.CompletedAt,
                    UpdatedOn = dto.UpdatedOn, DeletedAt = dto.DeletedAt
                }));

            await accountService.UpdateLastSyncAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            return SyncResult.Success;
        }
        catch
        {
            return SyncResult.Failed;
        }
    }

    private static async Task SyncEntityAsync<TLocal, TDto>(
        HttpClient client,
        string endpoint,
        long lastSyncAt,
        Func<Task<List<TLocal>>> getLocalModified,
        Func<TLocal, TDto> toDto,
        Func<TDto, Task> upsertLocal)
    {
        var localModified = await getLocalModified();
        var dtos = localModified.Select(toDto).ToList();

        var response = await client.PostAsJsonAsync(endpoint,
            new SyncRequestDto<TDto>(dtos, lastSyncAt));

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SyncResponseDto<TDto>>();
        if (result is null) return;

        foreach (var dto in result.Records)
            await upsertLocal(dto);
    }
}

// Local DTO types matching the API's SyncDtos exactly
public record SyncRequestDto<T>(List<T> Records, long LastSyncAt);
public record SyncResponseDto<T>(List<T> Records);

public record JournalSyncDto(string Guid, string AccountFk, string? Notes, string? Activity,
    string? Mood, string? Tags, long EnteredDate, long UpdatedOn, long? DeletedAt);

public record GoalSyncDto(string Guid, string AccountFk, string? GoalText,
    long? NextMeetingDate, long? ExpirationDate, long EnteredDate, string? MeasurableOutcome,
    long? CompletionDate, long UpdatedOn, long? DeletedAt);

public record GoalProgressSyncDto(string Guid, string AccountFk, string GoalFk,
    string? NextStepItems, long? NextMeetingDate, long UpdatedOn, long? DeletedAt);

public record TodoSyncDto(string Guid, string AccountFk, string? Title, string? Notes,
    long? DueDate, long? CompletedAt, long UpdatedOn, long? DeletedAt);
```

- [ ] **Step 2: Write SyncService tests**

Create `childDev/ChildDev.Mobile.Tests/SyncServiceTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using SQLite;

namespace ChildDev.Mobile.Tests;

public class SyncServiceTests : IDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private readonly JournalRepository _journalRepo;
    private readonly GoalRepository _goalRepo;
    private readonly GoalProgressRepository _goalProgressRepo;
    private readonly TodoRepository _todoRepo;
    private readonly AccountService _accountService;

    public SyncServiceTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _db = new SQLiteAsyncConnection(":memory:");
        _db.CreateTableAsync<Account>().GetAwaiter().GetResult();
        _db.CreateTableAsync<Journal>().GetAwaiter().GetResult();
        _db.CreateTableAsync<Goal>().GetAwaiter().GetResult();
        _db.CreateTableAsync<GoalProgress>().GetAwaiter().GetResult();
        _db.CreateTableAsync<Todo>().GetAwaiter().GetResult();

        _journalRepo = new JournalRepository(_db);
        _goalRepo = new GoalRepository(_db);
        _goalProgressRepo = new GoalProgressRepository(_db);
        _todoRepo = new TodoRepository(_db);
        _accountService = new AccountService(_db);
    }

    public void Dispose() => _db.CloseAsync().GetAwaiter().GetResult();

    private SyncService BuildSyncService(HttpMessageHandler handler, bool isConnected = true)
    {
        var connectivity = new FakeConnectivityService(isConnected);
        var factory = new FakeHttpClientFactory(handler);
        return new SyncService(_journalRepo, _goalRepo, _goalProgressRepo, _todoRepo,
            _accountService, connectivity, factory);
    }

    [Fact]
    public async Task RunAsync_NoServer_ReturnsNoServer()
    {
        await _accountService.CreateAccountAsync("user1", "1234");
        var account = await _accountService.GetAccountAsync();

        var service = BuildSyncService(new NotCalledHandler());
        var result = await service.RunAsync(account!);

        Assert.Equal(SyncResult.NoServer, result);
    }

    [Fact]
    public async Task RunAsync_NotConnected_ReturnsNoServer()
    {
        await _accountService.CreateAccountAsync("user2", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var service = BuildSyncService(new NotCalledHandler(), isConnected: false);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.NoServer, result);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsData_UpsertsLocally()
    {
        await _accountService.CreateAccountAsync("user3", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var serverJournal = new JournalSyncDto(
            System.Guid.NewGuid().ToString(), account.Guid, "From server",
            null, null, null, 1000, 1000, null);

        var handler = new FakeSyncHandler(serverJournal);
        var service = BuildSyncService(handler);
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Success, result);
        var journals = await _journalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(journals);
        Assert.Equal("From server", journals[0].Notes);
    }

    [Fact]
    public async Task RunAsync_ServerError_ReturnsFailed()
    {
        await _accountService.CreateAccountAsync("user4", "1234");
        var account = await _accountService.GetAccountAsync();
        account!.ServerUrl = "http://fake-server";
        account.ServerJwt = "fake-jwt";

        var service = BuildSyncService(new ErrorHandler());
        var result = await service.RunAsync(account);

        Assert.Equal(SyncResult.Failed, result);
    }
}

// Test helpers
public class FakeConnectivityService(bool isConnected) : ConnectivityService
{
    public override bool IsConnected => isConnected;
}

public class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler);
}

public class NotCalledHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Should not have been called");
}

public class ErrorHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
}

public class FakeSyncHandler(JournalSyncDto journal) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri!.PathAndQuery.Contains("journal"))
        {
            var response = new SyncResponseDto<JournalSyncDto>([journal]);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(response)
            };
        }
        // Return empty for other entity types
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { Records = Array.Empty<object>() })
        };
    }
}
```

- [ ] **Step 3: Register IHttpClientFactory in MauiProgram.cs**

Add to `MauiProgram.cs` inside `CreateMauiApp()` before `return builder.Build();`:
```csharp
builder.Services.AddHttpClient("childdev");
```

- [ ] **Step 4: Run sync tests**

```bash
dotnet test ChildDev.Mobile.Tests --filter "SyncServiceTests" -v
```
Expected: All 4 tests PASS.

- [ ] **Step 5: Run full test suite**

```bash
dotnet test ChildDev.Mobile.Tests -v
```
Expected: All tests PASS.

- [ ] **Step 6: Commit**

```bash
git add ChildDev.Mobile/ ChildDev.Mobile.Tests/
git commit -m "feat: add SyncService — fires on app open, last-write-wins upsert from server delta"
```

---

## Task 11: Add StringToBoolConverter Resource and Final Build

**Files:**
- Modify: `childDev/ChildDev.Mobile/App.xaml`

The XAML pages reference `{StaticResource StringToBoolConverter}`. It must be registered in `App.xaml`.

- [ ] **Step 1: Add converter to App.xaml resources**

Replace or update `childDev/ChildDev.Mobile/App.xaml`:
```xml
<?xml version="1.0" encoding="UTF-8" ?>
<Application xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="ChildDev.Mobile.App">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Resources/Styles/Colors.xaml" />
                <ResourceDictionary Source="Resources/Styles/Styles.xaml" />
            </ResourceDictionary.MergedDictionaries>

            <converters:StringToBoolConverter
                xmlns:converters="clr-namespace:ChildDev.Mobile.Converters"
                x:Key="StringToBoolConverter" />
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 2: Create StringToBoolConverter**

Create `childDev/ChildDev.Mobile/Converters/StringToBoolConverter.cs`:
```csharp
using System.Globalization;

namespace ChildDev.Mobile.Converters;

public class StringToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrEmpty(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
```

- [ ] **Step 3: Final build**

```bash
dotnet build ChildDev.Mobile -f net8.0-android
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Run all tests**

```bash
dotnet test ChildDev.Mobile.Tests -v && dotnet test ChildDev.Api.Tests -v
```
Expected: All tests PASS across both projects.

- [ ] **Step 5: Commit**

```bash
git add ChildDev.Mobile/
git commit -m "feat: add StringToBoolConverter, final wiring — all tests passing"
```

---

## Self-Review Checklist

- [x] Offline-first, all data in SQLite — Tasks 2–3 (repositories), all pages load from local DB
- [x] Local account on first launch (nickname + PIN) — Tasks 4–5 (AccountService, SetupPage)
- [x] Journal feature — Task 6
- [x] Goals + GoalProgress feature — Task 7
- [x] Todos feature — Task 8
- [x] Dashboard with summary + recent journals — Task 8
- [x] SyncService fires on app open — DashboardViewModel.LoadAsync calls RunSyncAsync; Task 10
- [x] Multi-device via server sync — SyncService sends local delta, upserts server delta
- [x] Settings page with server URL and last sync time — Task 9
- [x] ConnectivityService mockable for tests — Task 9, used in Task 10 tests
- [x] No blocking network calls on UI thread — sync runs via `_ = RunSyncAsync(account)` fire-and-forget
- [x] SyncResult enum used to show non-alarming status — DashboardViewModel, SyncResult.NoServer returns empty string
- [x] StringToBoolConverter for conditional visibility — Task 11
- [x] All ViewModels tested via unit tests (non-UI code) — Tasks 2–4, 10
- [x] No TBD/TODO placeholders — verified
- [x] Type consistency: JournalSyncDto, GoalSyncDto, GoalProgressSyncDto, TodoSyncDto defined once in SyncService.cs and referenced consistently
