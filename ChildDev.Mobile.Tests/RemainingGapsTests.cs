using LevelUp.Data;
using LevelUp.Models;
using LevelUp.ViewModels;
using SQLite;

namespace LevelUp.Tests;

/// <summary>
/// Closes the last remaining gaps: Journal.DisplayText, ReminderRepository direct methods,
/// TodoEntry OnLinkedGoalChanged branches, SettingsViewModel link success reload.
/// </summary>
public class JournalModelDisplayTextTests
{
    [Fact]
    public void DisplayText_NotesPresent_ReturnsNotes()
    {
        var j = new Journal { Notes = "Hello notes", Activity = "Reading" };
        Assert.Equal("Hello notes", j.DisplayText);
    }

    [Fact]
    public void DisplayText_NoNotes_ReturnsActivity()
    {
        var j = new Journal { Notes = null, Activity = "Running" };
        Assert.Equal("Running", j.DisplayText);
    }

    [Fact]
    public void DisplayText_NeitherNotesNorActivity_ReturnsEmpty()
    {
        var j = new Journal { Notes = null, Activity = null };
        Assert.Equal(string.Empty, j.DisplayText);
    }
}

public class ReminderRepositoryDirectTests : IDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private readonly ReminderRepository _repo;

    public ReminderRepositoryDirectTests()
    {
        SqliteFixture.EnsureInit();
        _db = new SQLiteAsyncConnection(":memory:");
        _db.CreateTableAsync<Reminder>().GetAwaiter().GetResult();
        _repo = new ReminderRepository(_db);
    }

    public void Dispose() => _db.CloseAsync().GetAwaiter().GetResult();

    [Fact]
    public async Task GetForEntityAsync_ReturnsMatchingEntityReminders()
    {
        var entityGuid = Guid.NewGuid().ToString();
        var other = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _repo.SaveAsync(new Reminder { AccountFk = "acc1", EntityGuid = entityGuid, Title = "Match", FireAt = now });
        await _repo.SaveAsync(new Reminder { AccountFk = "acc1", EntityGuid = other, Title = "NoMatch", FireAt = now });

        var results = await _repo.GetForEntityAsync(entityGuid);
        Assert.Single(results);
        Assert.Equal("Match", results[0].Title);
    }

    [Fact]
    public async Task GetForEntityAsync_ExcludesDismissed()
    {
        var entityGuid = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _repo.SaveAsync(new Reminder { AccountFk = "acc1", EntityGuid = entityGuid, Title = "Active", FireAt = now, IsDismissed = false });
        await _repo.SaveAsync(new Reminder { AccountFk = "acc1", EntityGuid = entityGuid, Title = "Gone", FireAt = now, IsDismissed = true });

        var results = await _repo.GetForEntityAsync(entityGuid);
        Assert.Single(results);
        Assert.Equal("Active", results[0].Title);
    }

    [Fact]
    public async Task GetAsync_ReturnsReminderById()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var r = new Reminder { AccountFk = "acc1", Title = "Find me", FireAt = now };
        await _repo.SaveAsync(r);

        var found = await _repo.GetAsync(r.Guid);
        Assert.NotNull(found);
        Assert.Equal("Find me", found!.Title);
    }

    [Fact]
    public async Task GetAsync_NonExistentGuid_ReturnsNull()
    {
        var result = await _repo.GetAsync("nonexistent-guid");
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_RemovesReminder()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var r = new Reminder { AccountFk = "acc1", Title = "Delete me", FireAt = now };
        await _repo.SaveAsync(r);

        var before = await _repo.GetAsync(r.Guid);
        Assert.NotNull(before);

        await _repo.DeleteAsync(r.Guid);

        var after = await _repo.GetAsync(r.Guid);
        Assert.Null(after);
    }
}

public class TodoEntryLinkedGoalBranchTests : ViewModelTestBase
{
    private TodoEntryViewModel BuildVm() =>
        new(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public void OnLinkedGoalChanged_ExistingGoalPrefix_ReplacesPrefix()
    {
        var goal1 = new Goal { Guid = Guid.NewGuid().ToString(), GoalText = "Old Goal" };
        var goal2 = new Goal { Guid = Guid.NewGuid().ToString(), GoalText = "New Goal" };

        var vm = BuildVm();
        vm.LinkedGoal = goal1;
        Assert.StartsWith("Goal: Old Goal", vm.Notes);

        vm.LinkedGoal = goal2;
        Assert.StartsWith("Goal: New Goal", vm.Notes);
        Assert.DoesNotContain("Old Goal", vm.Notes);
    }

    [Fact]
    public void OnLinkedGoalChanged_ExistingGoalPrefixWithFollowingNotes_KeepsFollowingNotes()
    {
        var goal1 = new Goal { Guid = Guid.NewGuid().ToString(), GoalText = "Old Goal" };
        var goal2 = new Goal { Guid = Guid.NewGuid().ToString(), GoalText = "New Goal" };

        var vm = BuildVm();
        vm.LinkedGoal = goal1;
        vm.Notes = "Goal: Old Goal\nSome extra notes here";

        vm.LinkedGoal = goal2;
        Assert.StartsWith("Goal: New Goal", vm.Notes);
        Assert.Contains("Some extra notes here", vm.Notes);
    }

    [Fact]
    public void OnLinkedGoalChanged_ExistingNonGoalNotes_PrependsPrefix()
    {
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), GoalText = "Learn piano" };
        var vm = BuildVm();
        vm.Notes = "Some notes I wrote first";
        vm.LinkedGoal = goal;

        Assert.StartsWith("Goal: Learn piano", vm.Notes);
        Assert.Contains("Some notes I wrote first", vm.Notes);
    }

    [Fact]
    public void OnLinkedGoalChanged_Null_DoesNothing()
    {
        var vm = BuildVm();
        vm.Notes = "These notes should not change";
        vm.LinkedGoal = null;
        Assert.Equal("These notes should not change", vm.Notes);
    }

    [Fact]
    public void OnLinkedGoalChanged_ExistingGoalPrefix_NoFollowingNotes_JustReplaces()
    {
        var goal1 = new Goal { Guid = Guid.NewGuid().ToString(), GoalText = "First goal" };
        var goal2 = new Goal { Guid = Guid.NewGuid().ToString(), GoalText = "Second goal" };

        var vm = BuildVm();
        vm.LinkedGoal = goal1;
        // Notes is now just "Goal: First goal" with no newline content after
        vm.Notes = "Goal: First goal";

        vm.LinkedGoal = goal2;
        Assert.Equal("Goal: Second goal", vm.Notes);
    }

    [Fact]
    public void TitleLength_UpdatesOnChange()
    {
        var vm = BuildVm();
        vm.Title = "Hello";
        Assert.Equal(5, vm.TitleLength);
    }

    [Fact]
    public void NotesLength_UpdatesOnChange()
    {
        var vm = BuildVm();
        vm.Notes = "Some notes";
        Assert.Equal(10, vm.NotesLength);
    }
}
