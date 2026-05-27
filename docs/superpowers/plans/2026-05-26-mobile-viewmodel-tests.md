# Mobile ViewModel Tests & Offline Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce `INavigationService` to abstract MAUI Shell calls, update all ViewModels to use it, and add comprehensive unit and regression tests verifying all CRUD operations work fully offline.

**Architecture:** A thin `INavigationService` interface wraps `Shell.Current.GoToAsync`, `DisplayAlert`, and `DisplayPromptAsync`. A `MauiNavigationService` implements it for production. A `FakeNavigationService` in tests records calls and returns configurable responses. ViewModels inject `INavigationService` instead of calling `Shell.Current` directly. Tests use in-memory SQLite (following the existing pattern in SyncServiceTests) plus a `ViewModelTestBase` helper class.

**Tech Stack:** .NET MAUI, CommunityToolkit.Mvvm, xUnit, sqlite-net-pcl (in-memory), FakeHttpClientFactory (already defined in SyncServiceTests.cs)

---

## File Map

**New files:**
- `ChildDev.Mobile/Services/INavigationService.cs` — interface with GoToAsync, DisplayAlertAsync, AlertAsync, DisplayPromptAsync
- `ChildDev.Mobile/Services/MauiNavigationService.cs` — production impl delegating to Shell.Current
- `ChildDev.Mobile.Tests/ViewModelTestBase.cs` — in-memory DB setup + ViewModel factory helpers
- `ChildDev.Mobile.Tests/GoalListViewModelTests.cs` — tests for GoalListViewModel
- `ChildDev.Mobile.Tests/GoalEntryViewModelTests.cs` — tests for GoalEntryViewModel
- `ChildDev.Mobile.Tests/JournalViewModelTests.cs` — tests for JournalListViewModel + JournalEntryViewModel
- `ChildDev.Mobile.Tests/TodoViewModelTests.cs` — tests for TodoListViewModel + TodoEntryViewModel
- `ChildDev.Mobile.Tests/OfflineCapabilityTests.cs` — cross-entity offline CRUD regression tests

**Modified files:**
- `ChildDev.Mobile/MauiProgram.cs` — register INavigationService → MauiNavigationService
- `ChildDev.Mobile/ViewModels/GoalListViewModel.cs` — inject INavigationService
- `ChildDev.Mobile/ViewModels/GoalEntryViewModel.cs` — inject INavigationService
- `ChildDev.Mobile/ViewModels/JournalListViewModel.cs` — inject INavigationService
- `ChildDev.Mobile/ViewModels/JournalEntryViewModel.cs` — inject INavigationService
- `ChildDev.Mobile/ViewModels/TodoListViewModel.cs` — inject INavigationService
- `ChildDev.Mobile/ViewModels/TodoEntryViewModel.cs` — inject INavigationService
- `ChildDev.Mobile/ViewModels/DashboardViewModel.cs` — inject INavigationService

---

## Task 1: Create INavigationService and MauiNavigationService

**Files:**
- Create: `ChildDev.Mobile/Services/INavigationService.cs`
- Create: `ChildDev.Mobile/Services/MauiNavigationService.cs`

- [ ] **Step 1: Create INavigationService**

Create `ChildDev.Mobile/Services/INavigationService.cs`:
```csharp
namespace LevelUp.Services;

public interface INavigationService
{
    Task GoToAsync(string route);
    Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel);
    Task AlertAsync(string title, string message, string cancel);
    Task<string?> DisplayPromptAsync(string title, string message, string accept, string cancel, string placeholder, int maxLength);
}
```

- [ ] **Step 2: Create MauiNavigationService**

Create `ChildDev.Mobile/Services/MauiNavigationService.cs`:
```csharp
namespace LevelUp.Services;

public class MauiNavigationService : INavigationService
{
    public Task GoToAsync(string route) =>
        Shell.Current.GoToAsync(route);

    public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel) =>
        Shell.Current.DisplayAlert(title, message, accept, cancel);

    public Task AlertAsync(string title, string message, string cancel) =>
        Shell.Current.DisplayAlert(title, message, cancel);

    public Task<string?> DisplayPromptAsync(string title, string message, string accept, string cancel, string placeholder, int maxLength) =>
        Shell.Current.DisplayPromptAsync(title, message, accept: accept, cancel: cancel,
            placeholder: placeholder, maxLength: maxLength, keyboard: Keyboard.Text);
}
```

- [ ] **Step 3: Register in MauiProgram.cs**

In `ChildDev.Mobile/MauiProgram.cs`, add this line after the existing service registrations (before `builder.Build()`):

```csharp
builder.Services.AddSingleton<INavigationService, MauiNavigationService>();
```

- [ ] **Step 4: Build the MAUI project to verify**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
dotnet build ChildDev.Mobile/LevelUp.csproj /p:TargetFramework=net8.0 /p:SkipMauiTargets=true --nologo -v minimal 2>&1 | tail -20
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add ChildDev.Mobile/Services/INavigationService.cs ChildDev.Mobile/Services/MauiNavigationService.cs ChildDev.Mobile/MauiProgram.cs
git commit -m "feat: add INavigationService abstraction for testable ViewModel navigation"
```

---

## Task 2: Update GoalListViewModel and GoalEntryViewModel

**Files:**
- Modify: `ChildDev.Mobile/ViewModels/GoalListViewModel.cs`
- Modify: `ChildDev.Mobile/ViewModels/GoalEntryViewModel.cs`

- [ ] **Step 1: Update GoalListViewModel constructor and Shell.Current calls**

In `ChildDev.Mobile/ViewModels/GoalListViewModel.cs`, add `INavigationService nav` to the primary constructor and replace all `Shell.Current` calls:

Constructor signature change (add `INavigationService nav` after existing params):
```csharp
public partial class GoalListViewModel(
    GoalRepository repo,
    GoalProgressRepository progressRepo,
    AccountService accountService,
    SyncService syncService,
    MobileAnalyticsService analytics,
    INavigationService nav) : ObservableObject
```

Replace all four `Shell.Current` usages:
- `await Shell.Current.GoToAsync("goals/entry")` → `await nav.GoToAsync("goals/entry")`
- `await Shell.Current.GoToAsync($"goals/entry?guid={goal.Guid}")` → `await nav.GoToAsync($"goals/entry?guid={goal.Guid}")`
- `var confirmed = await Shell.Current.DisplayAlert("Delete Goal?", "Remove this goal and all its progress notes?", "Delete", "Cancel")` → `var confirmed = await nav.DisplayAlertAsync("Delete Goal?", "Remove this goal and all its progress notes?", "Delete", "Cancel")`
- `var note = await Shell.Current.DisplayPromptAsync("📝 Quick Note", ..., accept: "Save", cancel: "Cancel", placeholder: "What progress did you make?", maxLength: 500, keyboard: Keyboard.Text)` → `var note = await nav.DisplayPromptAsync("📝 Quick Note", $"Add a progress note for:\n\"{(goal.GoalText?.Length > 60 ? goal.GoalText[..60] + "…" : goal.GoalText)}\"", "Save", "Cancel", "What progress did you make?", 500)`
- `await Shell.Current.DisplayAlert("✅ Note Saved!", "Great work keeping your goal moving forward! 🌟", "OK")` → `await nav.AlertAsync("✅ Note Saved!", "Great work keeping your goal moving forward! 🌟", "OK")`

- [ ] **Step 2: Update GoalEntryViewModel constructor and Shell.Current calls**

In `ChildDev.Mobile/ViewModels/GoalEntryViewModel.cs`, add `INavigationService nav` to the primary constructor after `MobileAnalyticsService analytics`:
```csharp
public partial class GoalEntryViewModel(
    GoalRepository repo,
    GoalProgressRepository progressRepo,
    TodoRepository todoRepo,
    AccountService accountService,
    MobileAnalyticsService analytics,
    INavigationService nav) : ObservableObject
```

Replace all `Shell.Current` usages:
- `await Shell.Current.GoToAsync("..")` (in SaveAsync) → `await nav.GoToAsync("..")`
- `var title = await Shell.Current.DisplayPromptAsync("➕ Add Todo", ..., accept: "Add", cancel: "Cancel", placeholder: "What needs to be done?", maxLength: 200, keyboard: Keyboard.Text)` → `var title = await nav.DisplayPromptAsync("➕ Add Todo", $"For goal: \"{goalName}\"", "Add", "Cancel", "What needs to be done?", 200)`
- `await Shell.Current.DisplayAlert("🎉 Goal Complete!", ...)` (3-param, cancel is null) → `await nav.AlertAsync("🎉 Goal Complete!", $"Amazing work! You've achieved \"{goalName}\" — take a moment to celebrate this win! 🌟", "Celebrate! 🎊")`
- `await Shell.Current.GoToAsync("..")` (after MarkComplete alert) → `await nav.GoToAsync("..")`
- `var confirmed = await Shell.Current.DisplayAlert("Delete Goal?", "Remove this goal and all its progress notes?", "Delete", "Cancel")` → `var confirmed = await nav.DisplayAlertAsync("Delete Goal?", "Remove this goal and all its progress notes?", "Delete", "Cancel")`
- `await Shell.Current.GoToAsync("..")` (after delete) → `await nav.GoToAsync("..")`

- [ ] **Step 3: Build to verify**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
dotnet build ChildDev.Mobile/LevelUp.csproj /p:TargetFramework=net8.0 /p:SkipMauiTargets=true --nologo -v minimal 2>&1 | tail -20
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add ChildDev.Mobile/ViewModels/GoalListViewModel.cs ChildDev.Mobile/ViewModels/GoalEntryViewModel.cs
git commit -m "refactor: inject INavigationService into Goal ViewModels"
```

---

## Task 3: Update Journal, Todo, and Dashboard ViewModels

**Files:**
- Modify: `ChildDev.Mobile/ViewModels/JournalListViewModel.cs`
- Modify: `ChildDev.Mobile/ViewModels/JournalEntryViewModel.cs`
- Modify: `ChildDev.Mobile/ViewModels/TodoListViewModel.cs`
- Modify: `ChildDev.Mobile/ViewModels/TodoEntryViewModel.cs`
- Modify: `ChildDev.Mobile/ViewModels/DashboardViewModel.cs`

- [ ] **Step 1: Update JournalListViewModel**

Add `INavigationService nav` to constructor after `MobileAnalyticsService analytics`:
```csharp
public partial class JournalListViewModel(
    JournalRepository repo,
    AccountService accountService,
    SyncService syncService,
    MobileAnalyticsService analytics,
    INavigationService nav) : ObservableObject
```

Replace Shell.Current calls:
- `await Shell.Current.GoToAsync("journal/entry")` → `await nav.GoToAsync("journal/entry")`
- `await Shell.Current.GoToAsync($"journal/entry?guid={journal.Guid}")` → `await nav.GoToAsync($"journal/entry?guid={journal.Guid}")`
- `var confirmed = await Shell.Current.DisplayAlert("Delete Entry?", "Remove this journal entry?", "Delete", "Cancel")` → `var confirmed = await nav.DisplayAlertAsync("Delete Entry?", "Remove this journal entry?", "Delete", "Cancel")`

- [ ] **Step 2: Update JournalEntryViewModel**

Add `INavigationService nav` to constructor after `MobileAnalyticsService analytics`:
```csharp
public partial class JournalEntryViewModel(
    JournalRepository repo,
    AccountService accountService,
    MobileAnalyticsService analytics,
    INavigationService nav) : ObservableObject
```

Replace Shell.Current calls:
- `await Shell.Current.GoToAsync("..")` (in SaveAsync) → `await nav.GoToAsync("..")`
- `var confirmed = await Shell.Current.DisplayAlert("Delete Entry?", "Remove this journal entry?", "Delete", "Cancel")` → `var confirmed = await nav.DisplayAlertAsync("Delete Entry?", "Remove this journal entry?", "Delete", "Cancel")`
- `await Shell.Current.GoToAsync("..")` (after delete) → `await nav.GoToAsync("..")`

- [ ] **Step 3: Update TodoListViewModel**

Add `INavigationService nav` to constructor after `MobileAnalyticsService analytics`:
```csharp
public partial class TodoListViewModel(
    TodoRepository repo,
    AccountService accountService,
    SyncService syncService,
    MobileAnalyticsService analytics,
    INavigationService nav) : ObservableObject
```

Replace Shell.Current calls:
- `var confirmed = await Shell.Current.DisplayAlert("Delete Todo?", "Remove this todo?", "Delete", "Cancel")` → `var confirmed = await nav.DisplayAlertAsync("Delete Todo?", "Remove this todo?", "Delete", "Cancel")`
- `await Shell.Current.GoToAsync($"todos/entry?guid={todo.Guid}")` → `await nav.GoToAsync($"todos/entry?guid={todo.Guid}")`

- [ ] **Step 4: Update TodoEntryViewModel**

Add `INavigationService nav` to constructor after `MobileAnalyticsService analytics`:
```csharp
public partial class TodoEntryViewModel(
    TodoRepository repo,
    GoalRepository goalRepo,
    AccountService accountService,
    MobileAnalyticsService analytics,
    INavigationService nav) : ObservableObject
```

Replace all Shell.Current calls:
- `await Shell.Current.GoToAsync("..")` (SaveAsync) → `await nav.GoToAsync("..")`
- `await Shell.Current.GoToAsync("..")` (MarkDoneAsync) → `await nav.GoToAsync("..")`
- `await Shell.Current.GoToAsync("..")` (RestoreAsync) → `await nav.GoToAsync("..")`
- `var confirmed = await Shell.Current.DisplayAlert("Delete Todo?", "Remove this todo?", "Delete", "Cancel")` → `var confirmed = await nav.DisplayAlertAsync("Delete Todo?", "Remove this todo?", "Delete", "Cancel")`
- `await Shell.Current.GoToAsync("..")` (DeleteAsync) → `await nav.GoToAsync("..")`

- [ ] **Step 5: Update DashboardViewModel**

Add `INavigationService nav` to constructor after `MobileAnalyticsService analytics`:
```csharp
public partial class DashboardViewModel(
    JournalRepository journalRepo,
    GoalRepository goalRepo,
    GoalProgressRepository progressRepo,
    TodoRepository todoRepo,
    AccountService accountService,
    SyncService syncService,
    MobileAnalyticsService analytics,
    INavigationService nav) : ObservableObject
```

Replace all Shell.Current calls (7 occurrences):
- `await Shell.Current.GoToAsync($"goals/entry?guid={StaleGoalGuid}")` → `await nav.GoToAsync($"goals/entry?guid={StaleGoalGuid}")`
- `var note = await Shell.Current.DisplayPromptAsync("📝 Quick Note", ..., accept: "Save", cancel: "Cancel", placeholder: "What progress did you make?", maxLength: 500, keyboard: Keyboard.Text)` → `var note = await nav.DisplayPromptAsync("📝 Quick Note", $"Progress note for:\n\"{goalName}\"", "Save", "Cancel", "What progress did you make?", 500)`
- `await Shell.Current.GoToAsync("journal/entry")` → `await nav.GoToAsync("journal/entry")`
- `await Shell.Current.GoToAsync($"journal/entry?guid={journal.Guid}")` → `await nav.GoToAsync($"journal/entry?guid={journal.Guid}")`
- `await Shell.Current.GoToAsync("settings")` → `await nav.GoToAsync("settings")`
- `await Shell.Current.GoToAsync("//goals")` → `await nav.GoToAsync("//goals")`
- `await Shell.Current.GoToAsync("//todos")` → `await nav.GoToAsync("//todos")`
- `await Shell.Current.GoToAsync("//journal")` → `await nav.GoToAsync("//journal")`

- [ ] **Step 6: Build to verify all ViewModels compile**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
dotnet build ChildDev.Mobile/LevelUp.csproj /p:TargetFramework=net8.0 /p:SkipMauiTargets=true --nologo -v minimal 2>&1 | tail -20
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 7: Commit**

```bash
git add ChildDev.Mobile/ViewModels/JournalListViewModel.cs ChildDev.Mobile/ViewModels/JournalEntryViewModel.cs ChildDev.Mobile/ViewModels/TodoListViewModel.cs ChildDev.Mobile/ViewModels/TodoEntryViewModel.cs ChildDev.Mobile/ViewModels/DashboardViewModel.cs
git commit -m "refactor: inject INavigationService into all remaining ViewModels"
```

---

## Task 4: Create ViewModelTestBase and FakeNavigationService

**Files:**
- Create: `ChildDev.Mobile.Tests/ViewModelTestBase.cs`

- [ ] **Step 1: Write the failing test (baseline)**

Verify the test project still builds after the ViewModel refactor:

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
MSBuildEnableWorkloadResolver=false dotnet build ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true --nologo -v minimal 2>&1 | tail -20
```

Expected: Build succeeded. (If errors exist, fix before proceeding.)

- [ ] **Step 2: Create ViewModelTestBase.cs**

Create `ChildDev.Mobile.Tests/ViewModelTestBase.cs`:

```csharp
using LevelUp.Data;
using LevelUp.Models;
using LevelUp.Services;
using SQLite;
using System.Net;
using System.Net.Http.Json;

namespace LevelUp.Tests;

/// <summary>
/// In-memory DB + helpers shared by all ViewModel tests.
/// </summary>
public abstract class ViewModelTestBase : IDisposable
{
    protected readonly SQLiteAsyncConnection Db;
    protected readonly AccountService AccountService;
    protected readonly GoalRepository GoalRepo;
    protected readonly GoalProgressRepository GoalProgressRepo;
    protected readonly JournalRepository JournalRepo;
    protected readonly TodoRepository TodoRepo;
    protected readonly FakeNavigationService Nav;
    protected readonly MobileAnalyticsService Analytics;

    protected ViewModelTestBase()
    {
        SqliteFixture.EnsureInit();
        Db = new SQLiteAsyncConnection(":memory:");
        Db.CreateTableAsync<Account>().GetAwaiter().GetResult();
        Db.CreateTableAsync<Goal>().GetAwaiter().GetResult();
        Db.CreateTableAsync<GoalProgress>().GetAwaiter().GetResult();
        Db.CreateTableAsync<Journal>().GetAwaiter().GetResult();
        Db.CreateTableAsync<Todo>().GetAwaiter().GetResult();

        AccountService = new AccountService(Db);
        GoalRepo = new GoalRepository(Db);
        GoalProgressRepo = new GoalProgressRepository(Db);
        JournalRepo = new JournalRepository(Db);
        TodoRepo = new TodoRepository(Db);
        Nav = new FakeNavigationService();
        Analytics = new MobileAnalyticsService(AccountService, new FakeHttpClientFactory(new NoOpHttpHandler()));
    }

    protected SyncService BuildOfflineSyncService() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService,
            new FakeConnectivityService(false), new FakeHttpClientFactory(new NoOpHttpHandler()));

    protected async Task<Account> CreateTestAccountAsync(string nick = "TestUser", string pin = "1234")
    {
        await AccountService.CreateAccountAsync(nick, pin);
        return (await AccountService.GetAccountAsync())!;
    }

    public void Dispose() => Db.CloseAsync().GetAwaiter().GetResult();
}

/// <summary>
/// Records all navigation calls. Configurable alert/prompt responses.
/// </summary>
public class FakeNavigationService : INavigationService
{
    public List<string> NavigatedRoutes { get; } = [];
    public List<string> AlertTitles { get; } = [];
    public List<string> PromptTitles { get; } = [];

    public bool AlertConfirmResult { get; set; } = true;
    public string? PromptResult { get; set; } = "Test note";

    public Task GoToAsync(string route)
    {
        NavigatedRoutes.Add(route);
        return Task.CompletedTask;
    }

    public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel)
    {
        AlertTitles.Add(title);
        return Task.FromResult(AlertConfirmResult);
    }

    public Task AlertAsync(string title, string message, string cancel)
    {
        AlertTitles.Add(title);
        return Task.CompletedTask;
    }

    public Task<string?> DisplayPromptAsync(string title, string message, string accept, string cancel, string placeholder, int maxLength)
    {
        PromptTitles.Add(title);
        return Task.FromResult(PromptResult);
    }
}

/// <summary>HTTP handler that always returns 200 with empty JSON.</summary>
public class NoOpHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { Records = Array.Empty<object>() })
        });
}
```

- [ ] **Step 3: Build test project**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
MSBuildEnableWorkloadResolver=false dotnet build ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true --nologo -v minimal 2>&1 | tail -20
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add ChildDev.Mobile.Tests/ViewModelTestBase.cs
git commit -m "test: add ViewModelTestBase and FakeNavigationService infrastructure"
```

---

## Task 5: GoalListViewModel Tests

**Files:**
- Create: `ChildDev.Mobile.Tests/GoalListViewModelTests.cs`

- [ ] **Step 1: Write the test file**

Create `ChildDev.Mobile.Tests/GoalListViewModelTests.cs`:

```csharp
using LevelUp.Data;
using LevelUp.Models;
using LevelUp.Services;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

public class GoalListViewModelTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_WithNoAccount_DoesNotThrow()
    {
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Empty(vm.Goals);
    }

    [Fact]
    public async Task Load_PopulatesGoalsFromLocalDb()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn piano", EnteredDate = now });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run 5k", EnteredDate = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Goals.Count);
        Assert.True(vm.HasGoals);
    }

    [Fact]
    public async Task Load_DoesNotRequireNetwork()
    {
        // SyncService is configured offline — Load must still populate goals
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Read books", EnteredDate = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Single(vm.Goals);
        Assert.Empty(vm.StatusMessage);
    }

    [Fact]
    public async Task FilterText_NarrowsGoalList()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn piano", EnteredDate = now });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run 5k", EnteredDate = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "piano";

        Assert.Single(vm.Goals);
        Assert.Equal("Learn piano", vm.Goals[0].GoalText);
    }

    [Fact]
    public async Task FilterText_CaseInsensitive()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn Piano", EnteredDate = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "PIANO";

        Assert.Single(vm.Goals);
    }

    [Fact]
    public async Task CategoryFilter_ShowsMatchingCategoryOnly()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Math homework", Category = "Academic", EnteredDate = now });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Daily walk", Category = "Health", EnteredDate = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.CategoryFilter = "Academic";

        Assert.Single(vm.Goals);
        Assert.Equal("Academic", vm.Goals[0].Category);
    }

    [Fact]
    public async Task CategoryFilter_All_ShowsAllGoals()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Math", Category = "Academic", EnteredDate = now });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", Category = "Health", EnteredDate = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.CategoryFilter = "Academic";
        vm.CategoryFilter = "All";

        Assert.Equal(2, vm.Goals.Count);
    }

    [Fact]
    public async Task Add_NavigatesToGoalEntry()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.AddCommand.ExecuteAsync(null);

        Assert.Contains("goals/entry", Nav.NavigatedRoutes);
        Assert.DoesNotContain(Nav.NavigatedRoutes, r => r.StartsWith("http"));
    }

    [Fact]
    public async Task Open_NavigatesToGoalEntryWithGuid()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.OpenCommand.ExecuteAsync(vm.Goals[0]);

        Assert.Contains(Nav.NavigatedRoutes, r => r.Contains("goals/entry") && r.Contains(goal.Guid));
    }

    [Fact]
    public async Task Delete_Confirmed_RemovesGoalFromList()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now });

        Nav.AlertConfirmResult = true;
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(vm.Goals[0]);

        Assert.Empty(vm.Goals);
    }

    [Fact]
    public async Task Delete_Cancelled_KeepsGoalInList()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now });

        Nav.AlertConfirmResult = false;
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(vm.Goals[0]);

        Assert.Single(vm.Goals);
    }

    [Fact]
    public async Task QuickNote_Confirmed_SavesProgressNote()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = "Made 2 miles today";
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.QuickNoteCommand.ExecuteAsync(vm.Goals[0]);

        var notes = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Single(notes);
        Assert.Equal("Made 2 miles today", notes[0].NextStepItems);
    }

    [Fact]
    public async Task QuickNote_Cancelled_SavesNothing()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = null;
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.QuickNoteCommand.ExecuteAsync(vm.Goals[0]);

        var notes = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Empty(notes);
    }

    [Fact]
    public async Task Refresh_WithNoServer_UpdatesGoals()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now });

        var vm = BuildVm();
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Single(vm.Goals);
        Assert.False(vm.IsRefreshing);
    }
}
```

- [ ] **Step 2: Run tests**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true --nologo -v normal --filter "GoalListViewModelTests" 2>&1 | tail -30
```

Expected: All tests PASS.

- [ ] **Step 3: Commit**

```bash
git add ChildDev.Mobile.Tests/GoalListViewModelTests.cs
git commit -m "test: add GoalListViewModel unit tests"
```

---

## Task 6: GoalEntryViewModel Tests

**Files:**
- Create: `ChildDev.Mobile.Tests/GoalEntryViewModelTests.cs`

- [ ] **Step 1: Write the test file**

Create `ChildDev.Mobile.Tests/GoalEntryViewModelTests.cs`:

```csharp
using LevelUp.Data;
using LevelUp.Models;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

public class GoalEntryViewModelTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav);

    [Fact]
    public void CanSave_EmptyGoalText_ReturnsFalse()
    {
        var vm = BuildVm();
        vm.GoalText = string.Empty;
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void CanSave_WithGoalText_ReturnsTrue()
    {
        var vm = BuildVm();
        vm.GoalText = "Learn piano";
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Save_NewGoal_PersistsToLocalDb()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.GoalText = "Learn piano";
        vm.Category = "Creative";
        await vm.SaveCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(goals);
        Assert.Equal("Learn piano", goals[0].GoalText);
        Assert.Equal("Creative", goals[0].Category);
    }

    [Fact]
    public async Task Save_NewGoal_NavigatesBack()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.GoalText = "Run 5k";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Contains("..", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task Save_NewGoalWithProgressNote_SavesProgressToo()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.GoalText = "Run 5k";
        vm.NextStepItems = "Start with 1 mile";
        await vm.SaveCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(goals);
        var notes = await GoalProgressRepo.GetForGoalAsync(goals[0].Guid);
        Assert.Single(notes);
        Assert.Equal("Start with 1 mile", notes[0].NextStepItems);
    }

    [Fact]
    public async Task Save_ExistingGoal_UpdatesRecord()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run 5k", EnteredDate = now };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(50); // let GuidChanged trigger LoadAsync
        vm.GoalText = "Run 10k";
        await vm.SaveCommand.ExecuteAsync(null);

        var updated = await GoalRepo.GetAsync(goal.Guid);
        Assert.Equal("Run 10k", updated!.GoalText);
    }

    [Fact]
    public async Task Load_ExistingGoal_PopulatesFields()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run 5k", Category = "Health", EnteredDate = now, UpdatedOn = now };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(100);

        Assert.Equal("Run 5k", vm.GoalText);
        Assert.Equal("Health", vm.Category);
        Assert.True(vm.IsExisting);
    }

    [Fact]
    public async Task Delete_Confirmed_SoftDeletesGoal()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now };
        await GoalRepo.SaveAsync(goal);

        Nav.AlertConfirmResult = true;
        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(50);
        await vm.DeleteCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Empty(goals);
    }

    [Fact]
    public async Task Delete_Cancelled_KeepsGoal()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now };
        await GoalRepo.SaveAsync(goal);

        Nav.AlertConfirmResult = false;
        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(50);
        await vm.DeleteCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(goals);
    }

    [Fact]
    public async Task MarkComplete_SetsCompletionDate()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(50);
        await vm.MarkCompleteCommand.ExecuteAsync(null);

        var updated = await GoalRepo.GetAsync(goal.Guid);
        Assert.NotNull(updated!.CompletionDate);
    }

    [Fact]
    public async Task AddLinkedTodo_Confirmed_SavesTodoWithGoalPrefix()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run 5k", EnteredDate = now };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = "Sign up for race";
        var vm = BuildVm();
        vm.Guid = goal.Guid;
        vm.GoalText = "Run 5k";
        await vm.AddLinkedTodoCommand.ExecuteAsync(null);

        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(todos);
        Assert.Equal("Sign up for race", todos[0].Title);
        Assert.StartsWith("Goal: Run 5k", todos[0].Notes);
    }
}
```

- [ ] **Step 2: Run tests**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true --nologo -v normal --filter "GoalEntryViewModelTests" 2>&1 | tail -30
```

Expected: All tests PASS.

- [ ] **Step 3: Commit**

```bash
git add ChildDev.Mobile.Tests/GoalEntryViewModelTests.cs
git commit -m "test: add GoalEntryViewModel unit tests"
```

---

## Task 7: Journal and Todo ViewModel Tests

**Files:**
- Create: `ChildDev.Mobile.Tests/JournalViewModelTests.cs`
- Create: `ChildDev.Mobile.Tests/TodoViewModelTests.cs`

- [ ] **Step 1: Write JournalViewModelTests.cs**

Create `ChildDev.Mobile.Tests/JournalViewModelTests.cs`:

```csharp
using LevelUp.Data;
using LevelUp.Models;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

public class JournalViewModelTests : ViewModelTestBase
{
    private JournalListViewModel BuildListVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    private JournalEntryViewModel BuildEntryVm() =>
        new(JournalRepo, AccountService, Analytics, Nav);

    [Fact]
    public async Task JournalList_Load_PopulatesJournals()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Great day", EnteredDate = now, UpdatedOn = now });

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Single(vm.Journals);
    }

    [Fact]
    public async Task JournalList_Load_DoesNotRequireNetwork()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Offline entry", EnteredDate = now, UpdatedOn = now });

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Single(vm.Journals);
        Assert.Empty(vm.StatusMessage);
    }

    [Fact]
    public async Task JournalList_FilterText_FiltersEntries()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Piano practice", EnteredDate = now, UpdatedOn = now });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Running track", EnteredDate = now, UpdatedOn = now });

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "piano";

        Assert.Single(vm.Journals);
    }

    [Fact]
    public async Task JournalList_Delete_Confirmed_RemovesEntry()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Entry", EnteredDate = now, UpdatedOn = now });

        Nav.AlertConfirmResult = true;
        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(vm.Journals[0]);

        Assert.Empty(vm.Journals);
    }

    [Fact]
    public async Task JournalList_Add_NavigatesToEntry()
    {
        await CreateTestAccountAsync();
        var vm = BuildListVm();
        await vm.AddCommand.ExecuteAsync(null);

        Assert.Contains("journal/entry", Nav.NavigatedRoutes);
        Assert.DoesNotContain(Nav.NavigatedRoutes, r => r.StartsWith("http"));
    }

    [Fact]
    public void JournalEntry_CanSave_WithNotes_ReturnsTrue()
    {
        var vm = BuildEntryVm();
        vm.Notes = "Today was productive";
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void JournalEntry_CanSave_EmptyNotesAndActivity_ReturnsFalse()
    {
        var vm = BuildEntryVm();
        vm.Notes = string.Empty;
        vm.Activity = string.Empty;
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task JournalEntry_Save_PersistsOffline()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildEntryVm();
        vm.Notes = "Learned something new";
        vm.Mood = "Happy";
        await vm.SaveCommand.ExecuteAsync(null);

        var journals = await JournalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(journals);
        Assert.Equal("Learned something new", journals[0].Notes);
        Assert.Equal("Happy", journals[0].Mood);
    }

    [Fact]
    public async Task JournalEntry_Save_NavigatesBack()
    {
        await CreateTestAccountAsync();
        var vm = BuildEntryVm();
        vm.Notes = "Good day";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Contains("..", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task JournalEntry_Load_PopulatesFields()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "My entry", Mood = "Calm", EnteredDate = now, UpdatedOn = now };
        await JournalRepo.SaveAsync(journal);

        var vm = BuildEntryVm();
        vm.Guid = journal.Guid;
        await Task.Delay(100);

        Assert.Equal("My entry", vm.Notes);
        Assert.Equal("Calm", vm.Mood);
        Assert.True(vm.IsExisting);
    }
}
```

- [ ] **Step 2: Write TodoViewModelTests.cs**

Create `ChildDev.Mobile.Tests/TodoViewModelTests.cs`:

```csharp
using LevelUp.Data;
using LevelUp.Models;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

public class TodoViewModelTests : ViewModelTestBase
{
    private TodoListViewModel BuildListVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    private TodoEntryViewModel BuildEntryVm() =>
        new(TodoRepo, GoalRepo, AccountService, Analytics, Nav);

    [Fact]
    public async Task TodoList_Load_PopulatesPendingTodos()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Buy milk", UpdatedOn = now });

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Single(vm.Todos);
    }

    [Fact]
    public async Task TodoList_Load_DoesNotRequireNetwork()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Offline task", UpdatedOn = now });

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Single(vm.Todos);
        Assert.Empty(vm.StatusMessage);
    }

    [Fact]
    public async Task TodoList_Add_InlineTitle_SavesTodo()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.NewTodoTitle = "Practice guitar";
        await vm.AddCommand.ExecuteAsync(null);

        Assert.Single(vm.Todos);
        Assert.Equal("Practice guitar", vm.Todos[0].Title);
        Assert.Empty(vm.NewTodoTitle); // field cleared after add
    }

    [Fact]
    public async Task TodoList_Complete_MovesToCompleted()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Buy milk", UpdatedOn = now });

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.CompleteCommand.ExecuteAsync(vm.Todos[0]);

        Assert.Empty(vm.Todos);
        Assert.Single(vm.CompletedTodos);
    }

    [Fact]
    public async Task TodoList_Delete_Confirmed_RemovesTodo()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Buy milk", UpdatedOn = now });

        Nav.AlertConfirmResult = true;
        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(vm.Todos[0]);

        Assert.Empty(vm.Todos);
    }

    [Fact]
    public async Task TodoList_FilterText_FiltersItems()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Buy milk", UpdatedOn = now });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Write code", UpdatedOn = now });

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "milk";

        Assert.Single(vm.Todos);
    }

    [Fact]
    public void TodoEntry_CanSave_EmptyTitle_ReturnsFalse()
    {
        var vm = BuildEntryVm();
        vm.Title = string.Empty;
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void TodoEntry_CanSave_WithTitle_ReturnsTrue()
    {
        var vm = BuildEntryVm();
        vm.Title = "Do something";
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task TodoEntry_Save_PersistsOffline()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildEntryVm();
        vm.Title = "Learn a song";
        await vm.SaveCommand.ExecuteAsync(null);

        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(todos);
        Assert.Equal("Learn a song", todos[0].Title);
    }

    [Fact]
    public async Task TodoEntry_Save_NavigatesBack()
    {
        await CreateTestAccountAsync();
        var vm = BuildEntryVm();
        vm.Title = "Do laundry";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Contains("..", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task TodoEntry_MarkDone_CompletesTodo()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Task", UpdatedOn = now };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildEntryVm();
        vm.Guid = todo.Guid;
        await Task.Delay(50);
        await vm.MarkDoneCommand.ExecuteAsync(null);

        var completed = await TodoRepo.GetCompletedAsync(account.Guid);
        Assert.Single(completed);
    }
}
```

- [ ] **Step 3: Run tests**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true --nologo -v normal --filter "JournalViewModelTests|TodoViewModelTests" 2>&1 | tail -30
```

Expected: All tests PASS.

- [ ] **Step 4: Commit**

```bash
git add ChildDev.Mobile.Tests/JournalViewModelTests.cs ChildDev.Mobile.Tests/TodoViewModelTests.cs
git commit -m "test: add Journal and Todo ViewModel unit tests"
```

---

## Task 8: Offline Capability Regression Tests

**Files:**
- Create: `ChildDev.Mobile.Tests/OfflineCapabilityTests.cs`

- [ ] **Step 1: Write OfflineCapabilityTests.cs**

Create `ChildDev.Mobile.Tests/OfflineCapabilityTests.cs`:

```csharp
using LevelUp.Data;
using LevelUp.Models;
using LevelUp.Services;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

/// <summary>
/// Regression tests verifying that all core CRUD operations work with zero
/// network connectivity. These tests exist to prevent any future code change
/// from accidentally requiring the server for create/read/update/delete operations.
/// </summary>
public class OfflineCapabilityTests : ViewModelTestBase
{
    [Fact]
    public async Task FullOfflineCycle_CreateAndRetrieveGoal()
    {
        // Arrange: no server configured
        var account = await CreateTestAccountAsync();
        var vm = new GoalEntryViewModel(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav);
        vm.GoalText = "Learn piano";
        vm.Category = "Creative";
        vm.NextStepItems = "First lesson this week";

        // Act: save offline
        await vm.SaveCommand.ExecuteAsync(null);

        // Assert: persisted to local DB
        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(goals);
        Assert.Equal("Learn piano", goals[0].GoalText);
        var notes = await GoalProgressRepo.GetForGoalAsync(goals[0].Guid);
        Assert.Single(notes);
        Assert.Equal("First lesson this week", notes[0].NextStepItems);
    }

    [Fact]
    public async Task FullOfflineCycle_CreateAndRetrieveJournal()
    {
        var account = await CreateTestAccountAsync();
        var vm = new JournalEntryViewModel(JournalRepo, AccountService, Analytics, Nav);
        vm.Notes = "Had a great practice session";
        vm.Mood = "Happy";
        vm.Activity = "Music";

        await vm.SaveCommand.ExecuteAsync(null);

        var journals = await JournalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(journals);
        Assert.Equal("Had a great practice session", journals[0].Notes);
        Assert.Equal("Happy", journals[0].Mood);
    }

    [Fact]
    public async Task FullOfflineCycle_CreateAndCompleteTodo()
    {
        var account = await CreateTestAccountAsync();
        var listVm = new TodoListViewModel(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await listVm.LoadCommand.ExecuteAsync(null);
        listVm.NewTodoTitle = "Practice scales";
        await listVm.AddCommand.ExecuteAsync(null);

        Assert.Single(listVm.Todos);
        await listVm.CompleteCommand.ExecuteAsync(listVm.Todos[0]);

        Assert.Empty(listVm.Todos);
        var completed = await TodoRepo.GetCompletedAsync(account.Guid);
        Assert.Single(completed);
        Assert.NotNull(completed[0].CompletedAt);
    }

    [Fact]
    public async Task FullOfflineCycle_GoalProgressNote()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run 5k", EnteredDate = now };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = "Did 2 miles";
        var listVm = new GoalListViewModel(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await listVm.LoadCommand.ExecuteAsync(null);
        await listVm.QuickNoteCommand.ExecuteAsync(listVm.Goals[0]);

        var notes = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Single(notes);
        Assert.Equal("Did 2 miles", notes[0].NextStepItems);
    }

    [Fact]
    public async Task SyncFailure_DoesNotAffectLocalData()
    {
        // Arrange: create data offline
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Day 1", EnteredDate = now, UpdatedOn = now });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Task", UpdatedOn = now });

        // Act: sync fails
        var offlineSync = BuildOfflineSyncService();
        var result = await offlineSync.RunAsync(account);

        // Assert: local data untouched, sync result is NoServer (not an error)
        Assert.Equal(SyncResult.NoServer, result);
        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        var journals = await JournalRepo.GetAllActiveAsync(account.Guid);
        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(goals);
        Assert.Single(journals);
        Assert.Single(todos);
    }

    [Fact]
    public async Task Navigation_NeverProducesExternalUrl()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run", EnteredDate = now });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Entry", EnteredDate = now, UpdatedOn = now });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Task", UpdatedOn = now });

        // Exercise all list navigation commands
        var goalListVm = new GoalListViewModel(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await goalListVm.LoadCommand.ExecuteAsync(null);
        await goalListVm.AddCommand.ExecuteAsync(null);
        await goalListVm.OpenCommand.ExecuteAsync(goalListVm.Goals[0]);

        var journalListVm = new JournalListViewModel(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await journalListVm.LoadCommand.ExecuteAsync(null);
        await journalListVm.AddCommand.ExecuteAsync(null);
        await journalListVm.OpenCommand.ExecuteAsync(journalListVm.Journals[0]);

        // Assert: every navigated route is an internal Shell route
        foreach (var route in Nav.NavigatedRoutes)
        {
            Assert.False(route.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
                $"Navigation to external URL detected: {route}");
            Assert.False(route.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
                $"Navigation to external URL detected: {route}");
        }
    }

    [Fact]
    public async Task MultipleEntities_AllOffline_AllPersist()
    {
        var account = await CreateTestAccountAsync();

        // Create one of each entity type, all offline
        var goalVm = new GoalEntryViewModel(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav);
        goalVm.GoalText = "Play guitar";
        await goalVm.SaveCommand.ExecuteAsync(null);

        var journalVm = new JournalEntryViewModel(JournalRepo, AccountService, Analytics, Nav);
        journalVm.Notes = "Practiced for 30 minutes";
        await journalVm.SaveCommand.ExecuteAsync(null);

        var todoListVm = new TodoListViewModel(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await todoListVm.LoadCommand.ExecuteAsync(null);
        todoListVm.NewTodoTitle = "Learn chord G";
        await todoListVm.AddCommand.ExecuteAsync(null);

        // Verify all persisted
        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        var journals = await JournalRepo.GetAllActiveAsync(account.Guid);
        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(goals);
        Assert.Single(journals);
        Assert.Single(todos);
    }
}
```

- [ ] **Step 2: Run tests**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true --nologo -v normal --filter "OfflineCapabilityTests" 2>&1 | tail -30
```

Expected: All tests PASS.

- [ ] **Step 3: Run full test suite**

```bash
cd /mnt/8TB_HDD_DATA/shared/src/childDev
MSBuildEnableWorkloadResolver=false dotnet test ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj /p:SkipMauiTargets=true --nologo -v normal 2>&1 | tail -30
```

Expected: All existing tests still pass + new tests pass.

- [ ] **Step 4: Commit**

```bash
git add ChildDev.Mobile.Tests/OfflineCapabilityTests.cs
git commit -m "test: add offline capability regression tests covering all entity types"
```

---

## Self-Review Checklist

- [x] **INavigationService covers all Shell.Current call patterns** used in ViewModels (GoToAsync, DisplayAlertAsync, AlertAsync, DisplayPromptAsync)
- [x] **All 7 ViewModels updated** (GoalList, GoalEntry, JournalList, JournalEntry, TodoList, TodoEntry, Dashboard)
- [x] **MauiProgram.cs registers INavigationService** so production code has no broken DI
- [x] **FakeNavigationService records routes** enabling regression assertion that no external URLs are navigated to
- [x] **OfflineCapabilityTests.Navigation_NeverProducesExternalUrl** directly covers the reported bug scenario
- [x] **All test methods have actual code** — no TBDs or placeholders
- [x] **Method names consistent** throughout — `DisplayAlertAsync` (2 accept/cancel), `AlertAsync` (1 cancel/OK)
- [x] **ViewModelTestBase.BuildOfflineSyncService** uses `FakeConnectivityService(false)` to ensure tests never hit network
- [x] **Existing test infrastructure reused** — FakeConnectivityService, FakeHttpClientFactory, SqliteFixture all from existing tests
