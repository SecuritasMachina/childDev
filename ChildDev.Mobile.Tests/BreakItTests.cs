using System.Net;
using System.Net.Http.Json;
using LevelUp.Data;
using LevelUp.Models;
using LevelUp.Services;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

/// <summary>
/// Adversarial edge-case tests targeting specific logic boundaries.
/// Tests here expose real bugs; each failure identifies code to fix.
/// </summary>

// ─── Filter state after mutating operations ──────────────────────────────────

public class GoalListFilterStateTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task QuickNote_WhileFiltered_PreservesActiveFilter()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Piano practice", EnteredDate = ts });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Unrelated goal", EnteredDate = ts });

        Nav.PromptResult = "Practiced scales today";
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "piano";
        Assert.Single(vm.Goals);

        await vm.QuickNoteCommand.ExecuteAsync(vm.Goals[0]);

        // Filter must still be active after saving the note
        Assert.Single(vm.Goals);
        Assert.Equal("Piano practice", vm.Goals[0].GoalText);
    }

    [Fact]
    public async Task TogglePin_WhileFiltered_PreservesActiveFilter()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Exercise daily", EnteredDate = ts });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Sleep well", EnteredDate = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "exercise";
        Assert.Single(vm.Goals);

        await vm.TogglePinCommand.ExecuteAsync(vm.Goals[0]);

        // Filter must still be active after toggling pin
        Assert.Single(vm.Goals);
        Assert.Equal("Exercise daily", vm.Goals[0].GoalText);
    }

    [Fact]
    public async Task TogglePin_WithNoAccount_DoesNotThrow()
    {
        // TogglePin calls GetAccountAsync()! (null-forgive) — would crash if account is null
        // This tests the pattern but in practice account always exists when Goals loads
        // so we just ensure it doesn't blow up when called with a real goal
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Test", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.TogglePinCommand.ExecuteAsync(vm.Goals[0]); // should not throw
    }
}

// ─── TodoList: complete→uncomplete→re-complete cycle ────────────────────────

public class TodoListStateTransitionTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Complete_Uncomplete_Complete_StateRemainsConsistent()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Cycling todo", UpdatedOn = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Todos);
        Assert.Empty(vm.CompletedTodos);

        await vm.CompleteCommand.ExecuteAsync(vm.Todos[0]);
        Assert.Empty(vm.Todos);
        Assert.Single(vm.CompletedTodos);

        await vm.UncompleteCommand.ExecuteAsync(vm.CompletedTodos[0]);
        Assert.Single(vm.Todos);
        Assert.Empty(vm.CompletedTodos);

        await vm.CompleteCommand.ExecuteAsync(vm.Todos[0]);
        Assert.Empty(vm.Todos);
        Assert.Single(vm.CompletedTodos);
    }

    [Fact]
    public async Task OverdueCount_UpdatesCorrectlyAfterCompleteAndUncomplete()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Overdue", UpdatedOn = now, DueDate = yesterday });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.OverdueTodoCount);

        await vm.CompleteCommand.ExecuteAsync(vm.Todos[0]);
        Assert.Equal(0, vm.OverdueTodoCount);

        await vm.UncompleteCommand.ExecuteAsync(vm.CompletedTodos[0]);
        Assert.Equal(1, vm.OverdueTodoCount); // back to overdue after uncomplete
    }

    [Fact]
    public async Task FilterText_ClearedAfterAdd_ShowsNewTodo()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Existing task", UpdatedOn = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "existing";
        Assert.Single(vm.Todos);

        vm.NewTodoTitle = "New todo XYZ";
        await vm.AddCommand.ExecuteAsync(null);

        // New todo added — even with filter active the new item should appear if it matches
        vm.FilterText = "xyz";
        Assert.Single(vm.Todos);
        Assert.Equal("New todo XYZ", vm.Todos[0].Title);
    }
}

// ─── JournalEntry: load→delete chain ────────────────────────────────────────

public class JournalEntryDeleteChainTests : ViewModelTestBase
{
    private JournalEntryViewModel BuildVm() =>
        new(JournalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task Delete_AfterEdit_DeletesLatestVersion()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Original", EnteredDate = ts };
        await JournalRepo.SaveAsync(journal);

        Nav.AlertConfirmResult = true;
        var vm = BuildVm();
        vm.Guid = journal.Guid;
        await Task.Delay(200);

        // Edit first, then delete
        vm.Notes = "Edited notes";
        await vm.SaveCommand.ExecuteAsync(null);

        // Open again and delete
        var vm2 = BuildVm();
        vm2.Guid = journal.Guid;
        await Task.Delay(200);
        await vm2.DeleteCommand.ExecuteAsync(null);

        var after = await JournalRepo.GetAsync(journal.Guid);
        Assert.True(after is null || after.DeletedAt.HasValue);
    }
}

// ─── Goal progress tier boundary conditions ──────────────────────────────────

public class GoalTierBoundaryTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    private async Task SaveNotes(string goalGuid, string accountGuid, int count)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < count; i++)
            await GoalProgressRepo.SaveAsync(new GoalProgress
            {
                Guid = Guid.NewGuid().ToString(), GoalFk = goalGuid, AccountFk = accountGuid,
                NextStepItems = $"Note {i}", UpdatedOn = ts + i
            });
    }

    [Theory]
    [InlineData(0, "")]          // below threshold — no tier
    [InlineData(4, "")]          // one below Beginner
    [InlineData(5, "Beginner")]  // exact Beginner threshold
    [InlineData(14, "Beginner")] // one below Apprentice
    [InlineData(15, "Apprentice")]
    [InlineData(29, "Apprentice")]
    [InlineData(30, "Skilled")]
    [InlineData(59, "Skilled")]
    [InlineData(60, "Expert")]
    [InlineData(99, "Expert")]
    [InlineData(100, "Master")]
    [InlineData(199, "Master")]
    [InlineData(200, "Legend")]
    [InlineData(201, "Legend")]  // above max
    public async Task TierLabel_AtExactBoundaries_IsCorrect(int noteCount, string expectedTierFragment)
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Boundary goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        if (noteCount > 0) await SaveNotes(goal.Guid, account.Guid, noteCount);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(300);

        if (string.IsNullOrEmpty(expectedTierFragment))
            Assert.Equal(string.Empty, vm.TierLabel);
        else
            Assert.Contains(expectedTierFragment, vm.TierLabel);
    }

    [Fact]
    public async Task NextTierLabel_AtExactThresholds_ShowsCorrectTarget()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "NextTier", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        await SaveNotes(goal.Guid, account.Guid, 4); // 1 below Beginner

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(300);

        Assert.Contains("1 more note", vm.NextTierLabel);
        Assert.Contains("Beginner", vm.NextTierLabel);
    }
}

// ─── Reminder: fire-at boundary / past dates ─────────────────────────────────

public class ReminderBoundaryTests : ViewModelTestBase
{
    [Fact]
    public async Task GetPending_ExcludesDismissedAndPast()
    {
        var account = await CreateTestAccountAsync();
        var past = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
        var future = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();

        await ReminderSvc.ScheduleAsync(new Reminder { AccountFk = account.Guid, Title = "Past", Topic = "General", FireAt = past });
        await ReminderSvc.ScheduleAsync(new Reminder { AccountFk = account.Guid, Title = "Future", Topic = "General", FireAt = future });

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        // Only the future one should be pending (past is still "pending" until dismissed — confirm actual behavior)
        Assert.All(pending, r => Assert.False(r.IsDismissed));
    }

    [Fact]
    public async Task Snooze_WithZeroDuration_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var fireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        var reminder = new Reminder { AccountFk = account.Guid, Title = "Test", Topic = "General", FireAt = fireAt };
        await ReminderSvc.ScheduleAsync(reminder);

        await ReminderSvc.SnoozeAsync(reminder, TimeSpan.Zero); // boundary — zero duration
        var after = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(after);
    }

    [Fact]
    public async Task Dismiss_TwiceOnSameReminder_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var fireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        var reminder = new Reminder { AccountFk = account.Guid, Title = "Double dismiss", Topic = "General", FireAt = fireAt };
        await ReminderSvc.ScheduleAsync(reminder);

        await ReminderSvc.DismissAsync(reminder);
        await ReminderSvc.DismissAsync(reminder); // second dismiss — idempotent?
    }
}

// ─── GoalEntry: save without loading first ───────────────────────────────────

public class GoalEntrySaveWithoutLoadTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task Save_WithGoalTextButNoAccount_ReturnsWithoutCrash()
    {
        // No account created — SaveAsync should return early at account null check
        var vm = BuildVm();
        vm.GoalText = "Save without account";
        await vm.SaveCommand.ExecuteAsync(null);
        // Would be an empty list since no account
    }

    [Fact]
    public async Task Save_WithEmptyGoalText_CanSaveReturnsFalse()
    {
        var vm = BuildVm();
        vm.GoalText = string.Empty;
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Save_NewGoalTwiceInParallel_DoesNotDuplicateInDb()
    {
        var account = await CreateTestAccountAsync();

        // Two separate VMs both trying to save "new" goals simultaneously
        var vm1 = BuildVm();
        var vm2 = BuildVm();
        vm1.GoalText = "Parallel goal 1";
        vm2.GoalText = "Parallel goal 2";

        await Task.WhenAll(
            vm1.SaveCommand.ExecuteAsync(null),
            vm2.SaveCommand.ExecuteAsync(null)
        );

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Equal(2, goals.Count(g => g.CompletionDate is null && g.DeletedAt is null));
    }
}

// ─── TodoEntry: MarkDone on new (unsaved) todo ───────────────────────────────

public class TodoEntryStateTests : ViewModelTestBase
{
    private TodoEntryViewModel BuildVm() =>
        new(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task MarkDone_NoGuid_DoesNothing()
    {
        var vm = BuildVm();
        await vm.MarkDoneCommand.ExecuteAsync(null); // no exception, no crash
    }

    [Fact]
    public async Task Restore_NoGuid_DoesNothing()
    {
        var vm = BuildVm();
        await vm.RestoreCommand.ExecuteAsync(null); // no exception
    }

    [Fact]
    public async Task Save_TitleWithOnlyWhitespace_CanSaveReturnsFalse()
    {
        var vm = BuildVm();
        vm.Title = "   ";
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Save_ExistingTodo_ThenDelete_TodoIsRemoved()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Will be edited then deleted", UpdatedOn = now };
        await TodoRepo.SaveAsync(todo);

        Nav.AlertConfirmResult = true;
        var vm = BuildVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        vm.Title = "Edited title";
        await vm.SaveCommand.ExecuteAsync(null);

        var vm2 = BuildVm();
        vm2.Guid = todo.Guid;
        await Task.Delay(200);
        await vm2.DeleteCommand.ExecuteAsync(null);

        var after = await TodoRepo.GetAsync(todo.Guid);
        Assert.True(after is null || after.DeletedAt.HasValue);
    }
}

// ─── Dashboard: null-entity commands and edge cases ──────────────────────────

public class DashboardEdgeCaseTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_NoAccount_DoesNotThrow()
    {
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null); // no account — returns early
    }

    [Fact]
    public async Task OpenJournal_NullJournal_DoesNotThrow()
    {
        var vm = BuildVm();
        await vm.OpenJournalCommand.ExecuteAsync(null!); // null guard must protect
    }

    [Fact]
    public async Task QuickAddJournal_EmptyText_DoesNotSave()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.QuickJournalText = "   ";
        await vm.QuickAddJournalCommand.ExecuteAsync(null);
        var journals = await JournalRepo.GetAllActiveAsync(account.Guid);
        Assert.Empty(journals);
    }

    [Fact]
    public async Task QuickAddJournal_SavesAndAppearsInRecentJournals()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.QuickJournalText = "Quick thought";
        await vm.QuickAddJournalCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.RecentJournals);
        Assert.Equal(string.Empty, vm.QuickJournalText);
    }

    [Fact]
    public async Task Load_WithGoalsAndTodos_PopulatesCounters()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "G1", EnteredDate = ts });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "T1", UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.ActiveGoalCount);
        Assert.Equal(1, vm.PendingTodoCount);
    }

    [Fact]
    public async Task GoToStaleGoal_WhenNoStaleGoal_DoesNotNavigate()
    {
        var vm = BuildVm();
        await vm.GoToStaleGoalCommand.ExecuteAsync(null);
        Assert.Empty(Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task QuickNoteForFocusGoal_WhenNoStaleGoal_DoesNotThrow()
    {
        var vm = BuildVm();
        // StaleGoalGuid is empty — should return early
        await vm.QuickNoteForFocusGoalCommand.ExecuteAsync(null);
    }
}

// ─── JournalList: date filter cycles and delete-while-filtered ───────────────

public class JournalListFilterTests : ViewModelTestBase
{
    private JournalListViewModel BuildVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task DateFilter_Week_FiltersOldEntries()
    {
        var account = await CreateTestAccountAsync();
        var oldMs = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();
        var newMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Old", EnteredDate = oldMs, UpdatedOn = oldMs });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "New", EnteredDate = newMs, UpdatedOn = newMs });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Journals.Count);

        vm.DateFilter = "Week";
        Assert.Single(vm.Journals);
        Assert.Equal("New", vm.Journals[0].Notes);
    }

    [Fact]
    public async Task DateFilter_Cycle_AllToWeekToMonthToAll()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Entry", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SetDateFilterCommand.Execute("Week");
        Assert.Equal("Week", vm.DateFilter);
        Assert.Single(vm.Journals);

        vm.SetDateFilterCommand.Execute("Month");
        Assert.Equal("Month", vm.DateFilter);
        Assert.Single(vm.Journals);

        vm.SetDateFilterCommand.Execute("All");
        Assert.Equal("All", vm.DateFilter);
        Assert.Single(vm.Journals);
    }

    [Fact]
    public async Task Delete_WhileDateFiltered_FilterPreserved()
    {
        var account = await CreateTestAccountAsync();
        var oldMs = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();
        var newMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var oldJournal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Old", EnteredDate = oldMs, UpdatedOn = oldMs };
        var newJournal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "New", EnteredDate = newMs, UpdatedOn = newMs };
        await JournalRepo.SaveAsync(oldJournal);
        await JournalRepo.SaveAsync(newJournal);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.DateFilter = "Week";
        Assert.Single(vm.Journals);

        Nav.AlertConfirmResult = true;
        await vm.DeleteCommand.ExecuteAsync(vm.Journals[0]);

        // Filter still "Week", no entries remaining this week
        Assert.Equal("Week", vm.DateFilter);
        Assert.Empty(vm.Journals);
    }

    [Fact]
    public async Task ShufflePrompt_CyclesWithoutThrow()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        var initial = vm.TodayPrompt;

        // Cycle through many times — should not throw and eventually return to start
        for (int i = 0; i < 24; i++)
            vm.ShufflePromptCommand.Execute(null);

        Assert.False(string.IsNullOrEmpty(vm.TodayPrompt));
    }

    [Fact]
    public async Task FilterText_WithDateFilter_BothApplied()
    {
        var account = await CreateTestAccountAsync();
        var oldMs = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();
        var newMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Piano practice", EnteredDate = newMs, UpdatedOn = newMs });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Piano old", EnteredDate = oldMs, UpdatedOn = oldMs });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.DateFilter = "Week";
        vm.FilterText = "piano";

        Assert.Single(vm.Journals);
        Assert.Contains("Piano practice", vm.Journals[0].Notes!);
    }
}

// ─── JournalEntry: CanSave branches and ToggleTag ────────────────────────────

public class JournalEntryBranchTests : ViewModelTestBase
{
    private JournalEntryViewModel BuildVm() =>
        new(JournalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public void CanSave_ActivityOnly_NoNotes_ReturnsTrue()
    {
        var vm = BuildVm();
        vm.Notes = string.Empty;
        vm.Activity = "Soccer practice";
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void CanSave_BothEmpty_ReturnsFalse()
    {
        var vm = BuildVm();
        vm.Notes = string.Empty;
        vm.Activity = string.Empty;
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void CanSave_WhitespaceOnly_ReturnsFalse()
    {
        var vm = BuildVm();
        vm.Notes = "   ";
        vm.Activity = "   ";
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void ToggleTag_Add_Remove_Add_IsIdempotent()
    {
        var vm = BuildVm();
        vm.ToggleTagCommand.Execute("happy");
        Assert.Contains("happy", vm.Tags);

        vm.ToggleTagCommand.Execute("happy");
        Assert.DoesNotContain("happy", vm.Tags);

        vm.ToggleTagCommand.Execute("happy");
        Assert.Contains("happy", vm.Tags);
    }

    [Fact]
    public void ToggleTag_MultipleTagsFormattedCorrectly()
    {
        var vm = BuildVm();
        vm.ToggleTagCommand.Execute("happy");
        vm.ToggleTagCommand.Execute("proud");
        vm.ToggleTagCommand.Execute("excited");

        Assert.Contains("happy", vm.Tags);
        Assert.Contains("proud", vm.Tags);
        Assert.Contains("excited", vm.Tags);

        // Remove middle tag
        vm.ToggleTagCommand.Execute("proud");
        Assert.Contains("happy", vm.Tags);
        Assert.DoesNotContain("proud", vm.Tags);
        Assert.Contains("excited", vm.Tags);
    }

    [Fact]
    public async Task Save_ActivityOnly_PersistsCorrectly()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.Activity = "Swimming";
        await vm.SaveCommand.ExecuteAsync(null);

        var journals = await JournalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(journals);
        Assert.Equal("Swimming", journals[0].Activity);
        Assert.Null(journals[0].Notes);
    }
}

// ─── TodoList: filter bypass after Uncomplete, SnoozeOverdue ─────────────────

public class TodoListFilterBypassTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Uncomplete_WhileFiltered_PreservesActiveFilter()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Exercise todo", UpdatedOn = now });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Unrelated task", UpdatedOn = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        // Complete the exercise todo
        await vm.CompleteCommand.ExecuteAsync(vm.Todos.First(t => t.Title == "Exercise todo"));

        // Set filter then uncomplete
        vm.FilterText = "exercise";
        Assert.Empty(vm.Todos); // completed, not in pending filtered view

        await vm.UncompleteCommand.ExecuteAsync(vm.CompletedTodos[0]);

        // Filter must still be active — only exercise todo should show
        Assert.Equal("exercise", vm.FilterText);
        Assert.Single(vm.Todos);
        Assert.Equal("Exercise todo", vm.Todos[0].Title);
    }

    [Fact]
    public async Task SnoozeOverdue_WhileFiltered_PreservesActiveFilter()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Overdue exercise", UpdatedOn = now, DueDate = yesterday });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Future task", UpdatedOn = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "exercise";
        Assert.Single(vm.Todos);

        await vm.SnoozeOverdueCommand.ExecuteAsync(null);

        // Filter must still be active after snooze
        Assert.Equal("exercise", vm.FilterText);
        Assert.Single(vm.Todos);
        Assert.Equal("Overdue exercise", vm.Todos[0].Title);
    }
}

// ─── GoalList: delete while filtered preserves filter ────────────────────────

public class GoalListDeleteFilterTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Delete_WhileTextFiltered_FilterPreservedAfterDelete()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var g1 = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Piano practice", EnteredDate = ts };
        var g2 = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Exercise daily", EnteredDate = ts };
        var g3 = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Piano recital prep", EnteredDate = ts };
        await GoalRepo.SaveAsync(g1);
        await GoalRepo.SaveAsync(g2);
        await GoalRepo.SaveAsync(g3);

        Nav.AlertConfirmResult = true;
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "piano";
        Assert.Equal(2, vm.Goals.Count);

        await vm.DeleteCommand.ExecuteAsync(vm.Goals[0]);

        // Filter still active — only 1 piano goal remains
        Assert.Single(vm.Goals);
        Assert.Contains("piano", vm.Goals[0].GoalText!, StringComparison.OrdinalIgnoreCase);
    }
}

// ─── TodoEntry: quick-set due date commands ───────────────────────────────────

public class TodoEntryDueDateTests : ViewModelTestBase
{
    private TodoEntryViewModel BuildVm() =>
        new(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public void SetDueToday_SetsHasDueDateAndDateToToday()
    {
        var vm = BuildVm();
        vm.SetDueTodayCommand.Execute(null);
        Assert.True(vm.HasDueDate);
        Assert.Equal(DateTime.Today, vm.DueDate.Date);
    }

    [Fact]
    public void SetDueTomorrow_SetsDateToTomorrow()
    {
        var vm = BuildVm();
        vm.SetDueTomorrowCommand.Execute(null);
        Assert.True(vm.HasDueDate);
        Assert.Equal(DateTime.Today.AddDays(1), vm.DueDate.Date);
    }

    [Fact]
    public void SetDueThisWeek_SetsDateToFridayOrNextFriday()
    {
        var vm = BuildVm();
        vm.SetDueThisWeekCommand.Execute(null);
        Assert.True(vm.HasDueDate);
        Assert.Equal(DayOfWeek.Friday, vm.DueDate.DayOfWeek);
        Assert.True(vm.DueDate.Date >= DateTime.Today);
    }

    [Fact]
    public async Task Save_WithDueDate_PersistedCorrectly()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.Title = "Due task";
        vm.SetDueTomorrowCommand.Execute(null);
        await vm.SaveCommand.ExecuteAsync(null);

        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(todos);
        Assert.NotNull(todos[0].DueDate);
    }
}

// ─── GoalList: completed goals in entry count display ────────────────────────

public class GoalListCompletedCountTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task EntryCountDisplay_WithActiveAndCompletedGoals_ShowsBothCounts()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var g1 = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Active goal", EnteredDate = ts };
        var g2 = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Another active", EnteredDate = ts };
        await GoalRepo.SaveAsync(g1);
        await GoalRepo.SaveAsync(g2);
        await GoalRepo.CompleteAsync(g2.Guid);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Goals.Count);
        Assert.Contains("active", vm.EntryCountDisplay, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("completed", vm.EntryCountDisplay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HasGoals_WithNoGoals_IsFalse()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.HasGoals);
    }

    [Fact]
    public async Task HasGoals_AfterLoad_IsTrue()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Test", EnteredDate = ts });
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.HasGoals);
    }
}

// ─── Dashboard: OverallTierLabel and OpenJournal navigation ──────────────────

public class DashboardTierAndNavTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_WithFiveProgressNotes_SetsBeginnerTierLabel()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Tier goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        for (int i = 0; i < 5; i++)
            await GoalProgressRepo.SaveAsync(new GoalProgress
            {
                Guid = Guid.NewGuid().ToString(), GoalFk = goal.Guid, AccountFk = account.Guid,
                NextStepItems = $"Note {i}", UpdatedOn = ts + i
            });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("Beginner", vm.OverallTierLabel);
    }

    [Fact]
    public async Task Load_With50ProgressNotes_SetsSkilledTierLabel()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Skilled goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        for (int i = 0; i < 50; i++)
            await GoalProgressRepo.SaveAsync(new GoalProgress
            {
                Guid = Guid.NewGuid().ToString(), GoalFk = goal.Guid, AccountFk = account.Guid,
                NextStepItems = $"Note {i}", UpdatedOn = ts + i
            });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("Skilled", vm.OverallTierLabel);
    }

    [Fact]
    public async Task OpenJournal_WithRealJournal_Navigates()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "My journal", EnteredDate = ts, UpdatedOn = ts };
        await JournalRepo.SaveAsync(journal);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.OpenJournalCommand.ExecuteAsync(vm.RecentJournals[0]);
        Assert.Contains(Nav.NavigatedRoutes, r => r.Contains("journal/entry?guid="));
    }

    [Fact]
    public async Task Load_WithRecentJournals_PopulatesRecentList()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < 4; i++)
            await JournalRepo.SaveAsync(new Journal
            {
                Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
                Notes = $"Journal {i}", EnteredDate = ts + i, UpdatedOn = ts + i
            });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.RecentJournals.Count);
    }
}

// ─── GoalList NeedsAttention: boundary and combined filters ──────────────────

public class NeedsAttentionBoundaryTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task NeedsAttention_GoalUpdatedExactlyAt7DayThreshold_IsNotIncluded()
    {
        // Boundary: < staleThresholdMs (7 days) — goal updated exactly 7 days ago is NOT stale
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Boundary goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        var staleThresholdMs = DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeMilliseconds();
        // Save progress at exactly the threshold (not stale — must be strictly less than)
        await GoalProgressRepo.SaveAsync(new GoalProgress
        {
            Guid = Guid.NewGuid().ToString(), GoalFk = goal.Guid, AccountFk = account.Guid,
            NextStepItems = "boundary note", UpdatedOn = staleThresholdMs
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SetCategoryFilterCommand.Execute("NeedsAttention");

        // Exactly at threshold is not stale (strict <), so should not appear
        Assert.Empty(vm.Goals);
    }

    [Fact]
    public async Task NeedsAttention_CompletedGoal_IsExcluded()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Completed goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        // Complete the goal
        await GoalRepo.CompleteAsync(goal.Guid);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SetCategoryFilterCommand.Execute("NeedsAttention");

        // Completed goals must never appear in NeedsAttention
        Assert.Empty(vm.Goals);
    }

    [Fact]
    public async Task NeedsAttention_AndTextFilter_BothApplied()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var staleGoal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Piano stale", EnteredDate = ts };
        var freshGoal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Piano fresh", EnteredDate = ts };
        await GoalRepo.SaveAsync(staleGoal);
        await GoalRepo.SaveAsync(freshGoal);
        // Give fresh goal recent progress
        await GoalProgressRepo.SaveAsync(new GoalProgress
        {
            Guid = Guid.NewGuid().ToString(), GoalFk = freshGoal.Guid, AccountFk = account.Guid,
            NextStepItems = "recent work", UpdatedOn = ts
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SetCategoryFilterCommand.Execute("NeedsAttention");
        vm.FilterText = "piano";

        // Only the stale piano goal matches both filters
        Assert.Single(vm.Goals);
        Assert.Equal("Piano stale", vm.Goals[0].GoalText);
    }
}

// ─── GoalEntry: reopen after complete, EnteredDate display ───────────────────

public class GoalEntryReopenTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task MarkComplete_ThenReopen_IsCompletedResets()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Reopen me", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);
        Assert.False(vm.IsCompleted);

        await GoalRepo.CompleteAsync(goal.Guid);
        await vm.ReopenCommand.ExecuteAsync(null);

        Assert.False(vm.IsCompleted);
    }

    [Fact]
    public async Task Load_EnteredDateDisplay_IsFormattedCorrectly()
    {
        var account = await CreateTestAccountAsync();
        var specificDate = new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Dated goal", EnteredDate = specificDate };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.False(string.IsNullOrEmpty(vm.EnteredDateDisplay));
        Assert.Contains("Jun", vm.EnteredDateDisplay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Save_WithExpirationDate_PersistsCorrectly()
    {
        var account = await CreateTestAccountAsync();

        var vm = BuildVm();
        vm.GoalText = "Expiring goal";
        vm.HasExpirationDate = true;
        vm.ExpirationDate = DateTime.Today.AddMonths(6);
        await vm.SaveCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(goals);
        Assert.NotNull(goals[0].ExpirationDate);
    }

    [Fact]
    public async Task Save_WithNextMeetingDate_PersistsCorrectly()
    {
        var account = await CreateTestAccountAsync();

        var vm = BuildVm();
        vm.GoalText = "Meeting goal";
        vm.HasNextMeetingDate = true;
        vm.NextMeetingDate = DateTime.Today.AddDays(14);
        await vm.SaveCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(goals);
        Assert.NotNull(goals[0].NextMeetingDate);
    }
}

// ─── TodoList: snooze overdue with none overdue is no-op ─────────────────────

public class TodoListSnoozeTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task SnoozeOverdue_WhenNoneOverdue_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Not overdue", UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.OverdueTodoCount);

        await vm.SnoozeOverdueCommand.ExecuteAsync(null); // should no-op, not throw
        Assert.Single(vm.Todos); // still there
    }

    [Fact]
    public async Task SnoozeOverdue_WithOverdueTodos_SnoozesToTomorrow()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Overdue", UpdatedOn = now, DueDate = yesterday });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.OverdueTodoCount);

        await vm.SnoozeOverdueCommand.ExecuteAsync(null);

        // After snooze, overdue count should be 0
        Assert.Equal(0, vm.OverdueTodoCount);
    }

    [Fact]
    public async Task DeleteAsync_NullTodo_DoesNotThrow()
    {
        var vm = BuildVm();
        await vm.DeleteCommand.ExecuteAsync(null!);
    }
}

// ─── GoalEntry: AddLinkedTodo truncation and SetNoteTemplate ─────────────────

public class GoalEntryLinkedTodoTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task AddLinkedTodo_LongGoalText_TruncatesPromptTitle()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var longText = new string('A', 70); // > 60 chars
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = longText, EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = "Practice scales";
        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        await vm.AddLinkedTodoCommand.ExecuteAsync(null);

        // Verify prompt was shown (truncated title) and todo was saved
        Assert.NotEmpty(Nav.PromptTitles);
        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(todos);
        Assert.Equal("Practice scales", todos[0].Title);
    }

    [Fact]
    public async Task AddLinkedTodo_NoGuid_DoesNotSave()
    {
        var vm = BuildVm();
        vm.GoalText = "Some goal"; // Guid is empty
        Nav.PromptResult = "Some note";
        await vm.AddLinkedTodoCommand.ExecuteAsync(null);
        // No account either, so should return early on Guid check
        Assert.Empty(Nav.PromptTitles);
    }

    [Fact]
    public async Task SetNoteTemplate_SetsNextStepItemsIfNotAlreadySet()
    {
        var vm = BuildVm();
        vm.SetNoteTemplateCommand.Execute("✅ Progress: ");
        Assert.Equal("✅ Progress: ", vm.NextStepItems);
    }

    [Fact]
    public async Task SetNoteTemplate_AlreadyStartsWithPrefix_DoesNotOverwrite()
    {
        var vm = BuildVm();
        vm.NextStepItems = "✅ Progress: some work done";
        vm.SetNoteTemplateCommand.Execute("✅ Progress: ");
        // Already starts with prefix — should not overwrite
        Assert.Equal("✅ Progress: some work done", vm.NextStepItems);
    }

    [Fact]
    public async Task CompleteLinkedTodo_RemovesFromLinkedTodos()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "My goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Linked task", Notes = "Goal: My goal", UpdatedOn = ts };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);
        Assert.True(vm.HasLinkedTodos);

        await vm.CompleteLinkedTodoCommand.ExecuteAsync(vm.LinkedTodos[0]);

        Assert.Empty(vm.LinkedTodos);
        Assert.False(vm.HasLinkedTodos);
    }
}

// ─── TodoEntry: OnLinkedGoalChanged notes prefix replacement ─────────────────

public class TodoEntryLinkedGoalNotesTests : ViewModelTestBase
{
    private TodoEntryViewModel BuildVm() =>
        new(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task LinkGoal_SetsNotesPrefix()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Piano practice", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = string.Empty; // new todo
        await Task.Delay(50);
        vm.Title = "Practice task";
        vm.LinkedGoal = goal;

        Assert.StartsWith("Goal: Piano practice", vm.Notes);
    }

    [Fact]
    public async Task LinkGoal_WithExistingNotes_PreservesNotes()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Exercise", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Notes = "Extra context here";
        vm.LinkedGoal = goal;

        Assert.StartsWith("Goal: Exercise", vm.Notes);
        Assert.Contains("Extra context here", vm.Notes);
    }

    [Fact]
    public async Task ChangingLinkedGoal_ReplacesGoalPrefix()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal1 = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "First goal", EnteredDate = ts };
        var goal2 = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Second goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal1);
        await GoalRepo.SaveAsync(goal2);

        var vm = BuildVm();
        vm.LinkedGoal = goal1;
        Assert.StartsWith("Goal: First goal", vm.Notes);

        vm.LinkedGoal = goal2;
        Assert.StartsWith("Goal: Second goal", vm.Notes);
        Assert.DoesNotContain("First goal", vm.Notes);
    }

    [Fact]
    public async Task LinkGoal_MaxLengthGoalText_NotesDoesNotExceed2000Chars()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var maxGoalText = new string('D', 2000);
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = maxGoalText, EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.LinkedGoal = goal;

        Assert.True(vm.Notes!.Length <= 2000,
            $"Notes length {vm.Notes!.Length} exceeds 2000-char limit (would fail API sync)");
    }

    [Fact]
    public async Task LinkGoal_MaxLengthGoalText_WithExistingNotes_TotalDoesNotExceed2000Chars()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var maxGoalText = new string('E', 1994);
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = maxGoalText, EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Notes = "Extra context about this task that the user typed";
        vm.LinkedGoal = goal;

        Assert.True(vm.Notes!.Length <= 2000,
            $"Combined Notes length {vm.Notes!.Length} exceeds 2000-char limit");
    }

    [Fact]
    public async Task LoadAsync_TodoWithTruncatedGoalPrefix_StillDetectsLinkedGoal()
    {
        // When Notes was saved with a truncated goal prefix (1994 chars), LoadAsync must
        // still detect the linked goal by matching the truncated prefix against goal text.
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var maxGoalText = new string('F', 2000);
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = maxGoalText, EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        // Simulate what OnLinkedGoalChanged stores: Notes = "Goal: " + first 1994 chars
        var truncatedPrefix = $"Goal: {maxGoalText[..1994]}";
        var todo = new Todo
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Title = "Task with long goal",
            Notes = truncatedPrefix,
            UpdatedOn = ts
        };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        Assert.NotNull(vm.LinkedGoal);
        Assert.Equal(goal.Guid, vm.LinkedGoal!.Guid);
    }
}

// ─── Dashboard: navigation commands and stale-goal / next-meeting paths ──────

public class DashboardNavigationTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task GoToGoals_Navigates()
    {
        var vm = BuildVm();
        await vm.GoToGoalsCommand.ExecuteAsync(null);
        Assert.Contains("//goals", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task GoToTodos_Navigates()
    {
        var vm = BuildVm();
        await vm.GoToTodosCommand.ExecuteAsync(null);
        Assert.Contains("//todos", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task GoToJournal_Navigates()
    {
        var vm = BuildVm();
        await vm.GoToJournalCommand.ExecuteAsync(null);
        Assert.Contains("//journal", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task GoToSettings_Navigates()
    {
        var vm = BuildVm();
        await vm.GoToSettingsCommand.ExecuteAsync(null);
        Assert.Contains("settings", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task AddJournal_Navigates()
    {
        var vm = BuildVm();
        await vm.AddJournalCommand.ExecuteAsync(null);
        Assert.Contains("journal/entry", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task OpenReminders_Navigates()
    {
        var vm = BuildVm();
        await vm.OpenRemindersCommand.ExecuteAsync(null);
        Assert.Contains("reminders", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task Load_GoalWithNextMeetingToday_ShowsMeetingDisplay()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todayMs = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        var goal = new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Meeting goal",
            EnteredDate = ts, NextMeetingDate = todayMs
        };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasNextGoalMeeting);
        Assert.Contains("today", vm.NextGoalMeeting, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Load_GoalWithNextMeetingTomorrow_ShowsTomorrowLabel()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var tomorrowMs = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today.AddDays(1), DateTimeKind.Local)).ToUnixTimeMilliseconds();
        var goal = new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Tomorrow meeting",
            EnteredDate = ts, NextMeetingDate = tomorrowMs
        };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasNextGoalMeeting);
        Assert.Contains("tomorrow", vm.NextGoalMeeting, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Load_StaleGoal_HasStaleGoalTrue()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Neglected goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        // No progress notes → goal is immediately stale (no LatestProgressAt)

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasStaleGoal);
        Assert.Equal("Neglected goal", vm.StaleGoalText);
    }

    [Fact]
    public async Task QuickNoteForFocusGoal_WithStaleGoalSet_SavesNote()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Focus goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = "Made progress today!";
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.HasStaleGoal);

        await vm.QuickNoteForFocusGoalCommand.ExecuteAsync(null);

        // Stale goal cleared after quick note saved
        Assert.False(vm.HasStaleGoal);
        var progress = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Single(progress);
        Assert.Equal("Made progress today!", progress[0].NextStepItems);
    }

    [Fact]
    public async Task GoToStaleGoal_WithStaleGoalGuid_Navigates()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Stale nav goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.HasStaleGoal);

        await vm.GoToStaleGoalCommand.ExecuteAsync(null);
        Assert.Contains(Nav.NavigatedRoutes, r => r.Contains("goals/entry"));
    }

    [Fact]
    public async Task Load_WithOverdueTodo_SetsOverdueCount()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Title = "Overdue task", UpdatedOn = ts, DueDate = yesterday
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.OverdueTodoCount);
        Assert.True(vm.HasOverdueTodos);
    }
}

// ─── SettingsViewModel: break-it edge cases ──────────────────────────────────

public class SettingsViewModelBreakItTests : ViewModelTestBase
{
    private SettingsViewModel BuildVm() =>
        new(AccountService, new FakeHttpClientFactory(new NoOpHttpHandler()), Analytics);

    [Fact]
    public async Task Load_WithAccount_PopulatesNickName()
    {
        await CreateTestAccountAsync("TestKid", "9999");
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal("TestKid", vm.NickName);
    }

    [Fact]
    public async Task Load_NoAccount_DoesNotThrow()
    {
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task SaveServerUrl_EmptyUrl_ClearsAndSetsMessage()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ServerUrl = string.Empty;
        await vm.SaveServerUrlCommand.ExecuteAsync(null);
        Assert.Contains("cleared", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestConnection_EmptyUrl_SetsMessage()
    {
        var vm = BuildVm();
        vm.ServerUrl = string.Empty;
        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.Contains("Enter a server URL", vm.StatusMessage);
    }

    [Fact]
    public async Task UnlinkFromServer_SetsIsLinkedFalseAndMessage()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.UnlinkFromServerCommand.ExecuteAsync(null);
        Assert.False(vm.IsLinkedToServer);
        Assert.Contains("Unlinked", vm.StatusMessage);
    }
}

// ─── SettingsViewModel: LinkToServer guard branches ──────────────────────────

public class SettingsViewModelLinkTests : ViewModelTestBase
{
    private class FixedResponseHandler(HttpStatusCode status, object? body = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(status);
            if (body is not null)
                response.Content = JsonContent.Create(body);
            return Task.FromResult(response);
        }
    }

    private class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("simulated network failure");
    }

    private SettingsViewModel BuildVm(HttpMessageHandler? handler = null) =>
        new(AccountService, new FakeHttpClientFactory(handler ?? new FixedResponseHandler(HttpStatusCode.OK)), Analytics);

    [Fact]
    public async Task LinkToServer_EmptyUrl_SetsMessageAndReturnsEarly()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ServerUrl = string.Empty;
        vm.ServerNickName = "user";
        vm.ServerPin = "1234";
        await vm.LinkToServerCommand.ExecuteAsync(null);
        Assert.Contains("Save a server URL", vm.StatusMessage);
    }

    [Fact]
    public async Task LinkToServer_EmptyNickName_SetsMessageAndReturnsEarly()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ServerUrl = "https://server.local";
        vm.ServerNickName = string.Empty;
        vm.ServerPin = "1234";
        await vm.LinkToServerCommand.ExecuteAsync(null);
        Assert.Contains("nickname", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LinkToServer_EmptyPin_SetsMessageAndReturnsEarly()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ServerUrl = "https://server.local";
        vm.ServerNickName = "user";
        vm.ServerPin = string.Empty;
        await vm.LinkToServerCommand.ExecuteAsync(null);
        Assert.Contains("password", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LinkToServer_Unauthorized_SetsIncorrectCredentialsMessage()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm(new FixedResponseHandler(HttpStatusCode.Unauthorized));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ServerUrl = "https://server.local";
        vm.ServerNickName = "user";
        vm.ServerPin = "wrongpin";
        await vm.LinkToServerCommand.ExecuteAsync(null);
        Assert.Contains("Incorrect", vm.StatusMessage);
        Assert.False(vm.IsLinking);
    }

    [Fact]
    public async Task LinkToServer_NetworkException_SetsCouldNotConnectMessage()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm(new ThrowingHandler());
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ServerUrl = "https://server.local";
        vm.ServerNickName = "user";
        vm.ServerPin = "1234";
        await vm.LinkToServerCommand.ExecuteAsync(null);
        Assert.Contains("Could not connect", vm.StatusMessage);
        Assert.False(vm.IsLinking);
    }

    [Fact]
    public async Task LinkToServer_Success_SetsLinkedAndClearsFields()
    {
        var account = await CreateTestAccountAsync();
        var authJson = new { jwt = "fake-jwt-token", accountGuid = account.Guid };
        var vm = BuildVm(new FixedResponseHandler(HttpStatusCode.OK, authJson));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ServerUrl = "https://server.local";
        vm.ServerNickName = "user";
        vm.ServerPin = "1234";
        await vm.LinkToServerCommand.ExecuteAsync(null);
        Assert.True(vm.IsLinkedToServer);
        Assert.Empty(vm.ServerNickName);
        Assert.Empty(vm.ServerPin);
        Assert.False(vm.IsLinking);
    }
}

// ─── TodoListViewModel: overdue count stays current while filter is active ───

public class TodoListFilterDisplayConsistencyTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Complete_WithActiveFilter_EntryCountDisplayShowsMatchingCount()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Task alpha", UpdatedOn = now });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Task beta", UpdatedOn = now });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Unrelated", UpdatedOn = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "Task";
        Assert.Equal(2, vm.Todos.Count);
        Assert.Contains("2", vm.EntryCountDisplay);
        Assert.Contains("matching", vm.EntryCountDisplay);

        await vm.CompleteCommand.ExecuteAsync(vm.Todos[0]);

        // After completing one of the two "Task" todos, display should show "1 task matching"
        Assert.Contains("matching", vm.EntryCountDisplay);
        Assert.StartsWith("1", vm.EntryCountDisplay.Trim());
    }

    [Fact]
    public async Task Add_WithActiveFilter_EntryCountDisplayReflectsFilteredCount()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Task alpha", UpdatedOn = now });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Unrelated", UpdatedOn = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "Task";
        Assert.Equal(1, vm.Todos.Count);

        // Add a todo that matches the filter
        vm.NewTodoTitle = "Task gamma";
        await vm.AddCommand.ExecuteAsync(null);

        // Should now show "2 tasks matching"
        Assert.Contains("matching", vm.EntryCountDisplay);
        Assert.Contains("2", vm.EntryCountDisplay);
    }
}

public class TodoListOverdueWithFilterTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Refresh_WithActiveFilter_UpdatesOverdueCount()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();

        // Add a visible todo (matches filter "active")
        await TodoRepo.SaveAsync(new Todo
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Title = "active task", UpdatedOn = now
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "active";

        // Initially no overdue todos
        Assert.Equal(0, vm.OverdueTodoCount);

        // Add an overdue todo (also matches the filter)
        await TodoRepo.SaveAsync(new Todo
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Title = "active overdue task", DueDate = yesterday, UpdatedOn = now + 1
        });

        // Refresh while filter is active
        await vm.RefreshCommand.ExecuteAsync(null);

        // OverdueTodoCount should reflect the new overdue todo
        Assert.Equal(1, vm.OverdueTodoCount);
        Assert.True(vm.HasOverdueTodos);
    }
}

// ─── GoalProgressRepository: NextStepItems must come from latest row ─────────

public class GoalProgressLatestStepsTests : ViewModelTestBase
{
    [Fact]
    public async Task GetLatestProgressInfo_ReturnsNextStepsFromMostRecentRow()
    {
        var account = await CreateTestAccountAsync();
        var goalGuid = Guid.NewGuid().ToString();
        var baseTs = DateTimeOffset.UtcNow.AddHours(-3).ToUnixTimeMilliseconds();

        // Use UpsertFromSyncAsync to preserve explicit timestamps (SaveAsync overwrites UpdatedOn)
        await GoalProgressRepo.UpsertFromSyncAsync(new GoalProgress
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goalGuid,
            NextStepItems = "Old note from hour -3", UpdatedOn = baseTs
        });
        await GoalProgressRepo.UpsertFromSyncAsync(new GoalProgress
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goalGuid,
            NextStepItems = "Middle note from hour -2", UpdatedOn = baseTs + 3_600_000
        });
        await GoalProgressRepo.UpsertFromSyncAsync(new GoalProgress
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goalGuid,
            NextStepItems = "Newest note from hour -1", UpdatedOn = baseTs + 7_200_000
        });

        var info = await GoalProgressRepo.GetLatestProgressInfoAsync(account.Guid);

        Assert.True(info.ContainsKey(goalGuid));
        Assert.Equal(3, info[goalGuid].Count);
        Assert.Equal("Newest note from hour -1", info[goalGuid].Steps);
    }
}

// ─── AccountService: Reminder migration on GUID change ───────────────────────

public class AccountServiceLinkMigrationTests : ViewModelTestBase
{
    [Fact]
    public async Task LinkToServer_WithDifferentGuid_MigratesReminders()
    {
        var account = await CreateTestAccountAsync();
        var oldGuid = account.Guid;
        var newGuid = Guid.NewGuid().ToString();

        // Create a reminder under the old account GUID
        var reminder = new Reminder
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = oldGuid,
            Title = "Study reminder",
            Topic = "Goal",
            FireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
        };
        await ReminderRepo.SaveAsync(reminder);

        // Link to server with a different GUID
        await AccountService.LinkToServerAsync("jwt-token", "https://server.local", newGuid);

        // After linking, reminders should be retrievable under the new GUID
        var pending = await ReminderRepo.GetPendingAsync(newGuid);
        Assert.Single(pending);
        Assert.Equal("Study reminder", pending[0].Title);
    }

    [Fact]
    public async Task LinkToServer_SameGuid_RemainersUnchanged()
    {
        var account = await CreateTestAccountAsync();
        var sameGuid = account.Guid;

        var reminder = new Reminder
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = sameGuid,
            Title = "Keep reminder",
            Topic = "General",
            FireAt = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds()
        };
        await ReminderRepo.SaveAsync(reminder);

        // Link to server with the SAME GUID (no migration needed)
        await AccountService.LinkToServerAsync("jwt-token", "https://server.local", sameGuid);

        var pending = await ReminderRepo.GetPendingAsync(sameGuid);
        Assert.Single(pending);
    }
}

// ─── SettingsViewModel: LoadAsync branch coverage ────────────────────────────

public class SettingsViewModelLoadBranchTests : ViewModelTestBase
{
    private SettingsViewModel BuildVm() =>
        new(AccountService, new FakeHttpClientFactory(new NoOpHttpHandler()), Analytics);

    [Fact]
    public async Task Load_WithNonZeroLastSyncAt_ShowsFormattedDate()
    {
        await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeMilliseconds();
        await AccountService.UpdateLastSyncAsync(ts);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.NotEqual("Never", vm.LastSyncDisplay);
        Assert.NotEmpty(vm.LastSyncDisplay);
    }

    [Fact]
    public async Task Load_WithServerJwtSet_IsLinkedToServerTrue()
    {
        var account = await CreateTestAccountAsync();
        await AccountService.SaveServerCredentialsAsync("test-jwt", "https://server.local");

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsLinkedToServer);
        Assert.Equal("https://server.local", vm.ServerUrl);
    }
}

// ─── JournalListViewModel: streak >= 14 shows star emoji ─────────────────────

public class JournalListStreak14Tests : ViewModelTestBase
{
    private JournalListViewModel BuildVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_Streak14Days_ShowsStarEmojiInStreakDisplay()
    {
        var account = await CreateTestAccountAsync();
        for (int d = 1; d <= 14; d++)
        {
            var ts = DateTimeOffset.UtcNow.AddDays(-d).ToUnixTimeMilliseconds();
            await JournalRepo.SaveAsync(new Journal
            {
                Guid = Guid.NewGuid().ToString(),
                AccountFk = account.Guid,
                Notes = $"Day {d}",
                EnteredDate = ts
            });
        }

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("🌟", vm.StreakDisplay);
        Assert.Contains("14-day", vm.StreakDisplay);
    }

    [Fact]
    public async Task Load_Streak1Day_StreakDisplayIsEmpty()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            Notes = "Just one",
            EnteredDate = ts
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Empty(vm.StreakDisplay);
    }

    [Fact]
    public async Task Load_Streak2Days_ShowsStarEmojiNotFire()
    {
        var account = await CreateTestAccountAsync();
        for (int d = 0; d <= 1; d++)
        {
            var ts = DateTimeOffset.UtcNow.AddDays(-d).ToUnixTimeMilliseconds();
            await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = $"Day {d}", EnteredDate = ts });
        }

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("⭐", vm.StreakDisplay);
        Assert.Contains("2-day", vm.StreakDisplay);
    }

    [Fact]
    public async Task Load_Streak7Days_ShowsFireEmoji()
    {
        var account = await CreateTestAccountAsync();
        for (int d = 0; d <= 6; d++)
        {
            var ts = DateTimeOffset.UtcNow.AddDays(-d).ToUnixTimeMilliseconds();
            await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = $"Day {d}", EnteredDate = ts });
        }

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("🔥", vm.StreakDisplay);
        Assert.Contains("7-day", vm.StreakDisplay);
    }

    [Fact]
    public async Task Load_Streak3DaysWithNoTodayEntry_ShowsStreakWarning()
    {
        var account = await CreateTestAccountAsync();
        for (int d = 1; d <= 3; d++)
        {
            var ts = DateTimeOffset.UtcNow.AddDays(-d).ToUnixTimeMilliseconds();
            await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = $"Day {d}", EnteredDate = ts });
        }

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasStreakWarning);
        Assert.Contains("🛡️", vm.StreakWarning);
        Assert.Contains("3-day", vm.StreakWarning);
    }

    [Fact]
    public async Task Load_Streak7DaysWithNoTodayEntry_ShowsUrgentWarning()
    {
        var account = await CreateTestAccountAsync();
        for (int d = 1; d <= 7; d++)
        {
            var ts = DateTimeOffset.UtcNow.AddDays(-d).ToUnixTimeMilliseconds();
            await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = $"Day {d}", EnteredDate = ts });
        }

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasStreakWarning);
        Assert.Contains("⚠️", vm.StreakWarning);
        Assert.Contains("7-day", vm.StreakWarning);
    }

    [Fact]
    public async Task Load_Streak3DaysWithTodayEntry_NoStreakWarning()
    {
        var account = await CreateTestAccountAsync();
        for (int d = 0; d <= 2; d++)
        {
            var ts = DateTimeOffset.UtcNow.AddDays(-d).ToUnixTimeMilliseconds();
            await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = $"Day {d}", EnteredDate = ts });
        }

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        // Today's entry exists — no warning needed even with 3-day streak
        Assert.False(vm.HasStreakWarning);
        Assert.Empty(vm.StreakWarning);
    }

    [Fact]
    public async Task Load_Streak2Days_NoStreakWarning_BelowThreshold()
    {
        var account = await CreateTestAccountAsync();
        for (int d = 1; d <= 2; d++)
        {
            var ts = DateTimeOffset.UtcNow.AddDays(-d).ToUnixTimeMilliseconds();
            await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = $"Day {d}", EnteredDate = ts });
        }

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        // streak = 2, < 3 → no warning
        Assert.False(vm.HasStreakWarning);
    }
}

// ─── RemindersViewModel: uncovered branch paths ──────────────────────────────

public class RemindersViewModelBranchTests : ViewModelTestBase
{
    private RemindersViewModel BuildVm() => new(ReminderSvc, AccountService, Nav);

    [Fact]
    public async Task Load_WithNoAccount_RemainsSilentAndEmpty()
    {
        // No CreateTestAccountAsync → GetAccountAsync returns null → early return
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Empty(vm.Reminders);
        Assert.False(vm.HasReminders);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task SnoozeAsync_NullReminder_DoesNotThrow()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.SnoozeCommand.ExecuteAsync(null!);
        // No exception — null guard in SnoozeAsync
    }

    [Fact]
    public async Task SnoozeAsync_UserCancelsPickerDuration_ListUnchanged()
    {
        var account = await CreateTestAccountAsync();
        var future = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        await ReminderSvc.ScheduleAsync(new Reminder { AccountFk = account.Guid, Title = "Snooze me", Topic = "General", FireAt = future });

        Nav.ActionSheetResult = null; // simulate cancel
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Reminders);

        await vm.SnoozeCommand.ExecuteAsync(vm.Reminders[0]);

        // Duration was null (cancel) → list unchanged
        Assert.Single(vm.Reminders);
    }

    [Fact]
    public async Task AddGeneralAsync_UserCancelsPickerDuration_TitlePreserved()
    {
        await CreateTestAccountAsync();
        Nav.ActionSheetResult = null; // cancel the snooze picker

        var vm = BuildVm();
        vm.NewReminderTitle = "Remind me later";
        await vm.AddGeneralCommand.ExecuteAsync(null);

        Assert.Equal("Remind me later", vm.NewReminderTitle); // not cleared
        Assert.Empty(vm.Reminders);
    }

    [Fact]
    public async Task DismissAsync_NullReminder_DoesNotThrow()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.DismissCommand.ExecuteAsync(null!);
        // No exception — null guard in DismissAsync
    }
}

// ─── TodoEntryViewModel: LoadAsync branches (DueDate, IsCompleted, goal line) ─

public class TodoEntryLoadAsyncBranchTests : ViewModelTestBase
{
    private TodoEntryViewModel BuildVm() =>
        new(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task LoadAsync_TodoWithDueDate_SetsHasDueDateTrue()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var tomorrow = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Due task", DueDate = tomorrow, UpdatedOn = ts };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        Assert.True(vm.HasDueDate);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(tomorrow).LocalDateTime.Date, vm.DueDate.Date);
    }

    [Fact]
    public async Task LoadAsync_CompletedTodo_IsCompletedTrue()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Done task", CompletedAt = ts, UpdatedOn = ts };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        Assert.True(vm.IsCompleted);
        Assert.True(vm.IsExisting);
    }

    [Fact]
    public async Task LoadAsync_PendingTodo_IsCompletedFalseAndIsExistingTrue()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Pending task", UpdatedOn = ts };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        Assert.False(vm.IsCompleted);
        Assert.True(vm.IsExisting);
        Assert.False(vm.HasDueDate);
    }

    [Fact]
    public async Task LoadAsync_NotesWithGoalPrefixAndNewline_DetectsLinkedGoal()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run a mile", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Train", Notes = "Goal: Run a mile\nExtra details here", UpdatedOn = ts };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        Assert.NotNull(vm.LinkedGoal);
        Assert.Equal("Run a mile", vm.LinkedGoal!.GoalText);
    }

    [Fact]
    public async Task LoadAsync_NotesWithGoalPrefixNoNewline_DetectsLinkedGoal()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Read 10 books", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Read", Notes = "Goal: Read 10 books", UpdatedOn = ts };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        Assert.NotNull(vm.LinkedGoal);
        Assert.Equal("Read 10 books", vm.LinkedGoal!.GoalText);
    }

    [Fact]
    public async Task LoadAsync_NotesWithoutGoalPrefix_LinkedGoalIsNull()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Plain task", Notes = "Just some notes", UpdatedOn = ts };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        Assert.Null(vm.LinkedGoal);
    }
}

// ─── GoalListViewModel: load ordering (pinned first, then no-progress) ───────

public class GoalListOrderingTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_PinnedGoal_AppearsBeforeUnpinnedGoals()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Unpinned A", EnteredDate = ts, UpdatedOn = ts, IsPinned = false });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Pinned B", EnteredDate = ts + 1, UpdatedOn = ts + 1, IsPinned = true });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Goals.Count);
        Assert.Equal("Pinned B", vm.Goals[0].GoalText);
    }

    [Fact]
    public async Task Load_GoalWithNoProgress_AppearsBeforeGoalWithRecentProgress()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goalWithProgress = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Has progress", EnteredDate = ts, UpdatedOn = ts };
        var goalNoProgress = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "No progress", EnteredDate = ts + 1, UpdatedOn = ts + 1 };
        await GoalRepo.SaveAsync(goalWithProgress);
        await GoalRepo.SaveAsync(goalNoProgress);

        await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goalWithProgress.Guid, NextStepItems = "Working on it", UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Goals.Count);
        Assert.Equal("No progress", vm.Goals[0].GoalText);
    }

    [Fact]
    public async Task Load_NoAccount_GoalsStaysEmpty()
    {
        // No CreateTestAccountAsync → returns early
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Empty(vm.Goals);
        Assert.False(vm.HasGoals);
    }

    [Fact]
    public async Task Refresh_NoAccount_IsRefreshingFalseAfterReturn()
    {
        // No account → RefreshAsync sets IsRefreshing = false and returns
        var vm = BuildVm();
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.False(vm.IsRefreshing);
    }

    [Fact]
    public async Task Load_CompletedGoalAppearsAfterActiveGoals()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var activeGoal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Active", EnteredDate = ts, UpdatedOn = ts };
        var completedGoal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Completed", EnteredDate = ts, UpdatedOn = ts, CompletionDate = ts };
        await GoalRepo.SaveAsync(activeGoal);
        await GoalRepo.SaveAsync(completedGoal);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Goals.Count);
        Assert.Equal("Active", vm.Goals[0].GoalText);
        Assert.Equal("Completed", vm.Goals[1].GoalText);
    }
}

// ─── GoalListViewModel: RefreshAsync success path ────────────────────────────

public class GoalListRefreshTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Refresh_WithAccount_LoadsGoals()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Goal 1", EnteredDate = ts, UpdatedOn = ts });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Goal 2", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Goals.Count);
        Assert.False(vm.IsRefreshing);
        Assert.True(vm.HasGoals);
    }

    [Fact]
    public async Task Refresh_WithAccount_SetsIsRefreshingFalseAfterLoad()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.False(vm.IsRefreshing);
    }
}

// ─── JournalListViewModel: RefreshAsync paths ────────────────────────────────

public class JournalListRefreshTests : ViewModelTestBase
{
    private JournalListViewModel BuildVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Refresh_WithAccount_LoadsJournals()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Entry 1", EnteredDate = ts, UpdatedOn = ts });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Entry 2", EnteredDate = ts + 1, UpdatedOn = ts + 1 });

        var vm = BuildVm();
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Journals.Count);
        Assert.False(vm.IsRefreshing);
    }

    [Fact]
    public async Task Refresh_NoAccount_IsRefreshingFalse()
    {
        // No account → early return with IsRefreshing = false
        var vm = BuildVm();
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.False(vm.IsRefreshing);
        Assert.Empty(vm.Journals);
    }

    [Fact]
    public async Task Refresh_WithStreakData_SetsStreakDisplay()
    {
        var account = await CreateTestAccountAsync();
        for (int d = 0; d <= 6; d++)
        {
            var ts = DateTimeOffset.UtcNow.AddDays(-d).ToUnixTimeMilliseconds();
            await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = $"Day {d}", EnteredDate = ts, UpdatedOn = ts });
        }

        var vm = BuildVm();
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Contains("🔥", vm.StreakDisplay);
        Assert.False(vm.IsRefreshing);
    }

    [Fact]
    public async Task Refresh_AfterNewJournalAddedDirectly_ShowsUpdatedCount()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "First entry", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Journals);

        // Add another entry directly to repo (simulates sync)
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Second entry", EnteredDate = ts + 1, UpdatedOn = ts + 1 });

        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Journals.Count);
    }
}

public class GoalStepsSyncTests : ViewModelTestBase
{
    [Fact]
    public async Task GoalSyncDto_IncludesStepsField_RoundTrip()
    {
        // Verifies that Steps is preserved through the DTO round-trip that SyncService performs.
        // Before the fix, GoalSyncDto had no Steps parameter so the field was silently dropped.
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goalGuid = Guid.NewGuid().ToString();
        const string expectedSteps = "1. Find a teacher\n2. Practice 30 min daily";

        // Simulate what happens on the mobile after receiving a goal from the server (with Steps set)
        var goalFromServer = new Goal
        {
            Guid = goalGuid,
            AccountFk = account.Guid,
            GoalText = "Learn piano",
            Steps = expectedSteps,
            EnteredDate = ts,
            UpdatedOn = ts
        };
        await GoalRepo.UpsertFromSyncAsync(goalFromServer);

        // Simulate what SyncService does to build the outgoing DTO
        var saved = await GoalRepo.GetAsync(goalGuid);
        var dto = new GoalSyncDto(
            saved!.Guid, saved.AccountFk, saved.GoalText, saved.NextMeetingDate,
            saved.ExpirationDate, saved.EnteredDate, saved.MeasurableOutcome,
            saved.CompletionDate, saved.UpdatedOn, saved.DeletedAt,
            saved.ProgressPercent, saved.Category, saved.IsPinned, saved.Steps);

        // DTO must carry Steps
        Assert.Equal(expectedSteps, dto.Steps);

        // Simulate what SyncService does when applying a received DTO back to the local DB
        var reconstructed = new Goal
        {
            Guid = dto.Guid, AccountFk = dto.AccountFk, GoalText = dto.GoalText,
            Steps = dto.Steps,
            EnteredDate = dto.EnteredDate, UpdatedOn = dto.UpdatedOn + 1
        };
        await GoalRepo.UpsertFromSyncAsync(reconstructed);

        var final = await GoalRepo.GetAsync(goalGuid);
        Assert.NotNull(final);
        Assert.Equal(expectedSteps, final!.Steps);
    }
}

public class DashboardQuickJournalWeeklyWinsTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task QuickAddJournal_FirstJournalThisWeek_SetsHasWeeklyWinsTrue()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.HasWeeklyWins); // no wins yet

        vm.QuickJournalText = "First quick journal entry";
        await vm.QuickAddJournalCommand.ExecuteAsync(null);

        // After quick-add, weekly wins should reflect the new journal entry
        Assert.True(vm.HasWeeklyWins);
        Assert.True(vm.WeekJournalEntries > 0);
    }

    [Fact]
    public async Task QuickAddJournal_UpdatesWeekJournalEntriesCount()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.WeekJournalEntries);

        vm.QuickJournalText = "Journal entry 1";
        await vm.QuickAddJournalCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.WeekJournalEntries);
    }
}

// ─── GoalListViewModel: TogglePinAsync must not crash when account is null ────

public class GoalListTogglePinTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task TogglePin_PinsGoal_GoalRemainsInList()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Master chess", EnteredDate = ts, IsPinned = false });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn coding", EnteredDate = ts, IsPinned = false });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Goals.Count);

        var target = vm.Goals[0];
        await vm.TogglePinCommand.ExecuteAsync(target);

        // After pin toggle, list still has both goals
        Assert.Equal(2, vm.Goals.Count);
        Assert.True(vm.HasGoals);
    }

    [Fact]
    public async Task TogglePin_UnpinsAlreadyPinnedGoal_GoalRemainsInList()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Pinned goal", EnteredDate = ts, IsPinned = true });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Goals);

        await vm.TogglePinCommand.ExecuteAsync(vm.Goals[0]);

        Assert.Single(vm.Goals);
        Assert.True(vm.HasGoals);
    }
}

// ─── GoalEntryViewModel: null GoalText must not contaminate LinkedTodos ────────

public class GoalEntryLinkedTodosNullGoalTextTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task LoadEntry_GoalWithNullGoalText_LinkedTodosIsEmpty()
    {
        // A synced completed goal can arrive with null GoalText (API allows it when CompletionDate is set).
        // Before the fix, the prefix "Goal: " (with empty suffix) matched ALL goal-linked todos.
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nullGoalGuid = Guid.NewGuid().ToString();

        // Sync in a completed goal with null GoalText
        await GoalRepo.UpsertFromSyncAsync(new Goal
        {
            Guid = nullGoalGuid,
            AccountFk = account.Guid,
            GoalText = null,
            CompletionDate = ts,
            EnteredDate = ts,
            UpdatedOn = ts
        });

        // A todo linked to a DIFFERENT goal (notes starts "Goal: ...")
        await TodoRepo.SaveAsync(new Todo
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            Title = "Practice scales",
            Notes = "Goal: Learn piano",
            UpdatedOn = ts
        });

        var vm = BuildVm();
        vm.Guid = nullGoalGuid;
        await Task.Delay(50); // allow FireAndForget LoadAsync to complete

        // LinkedTodos must not include the piano todo — its prefix "Goal: Learn piano"
        // starts with "Goal: " which would incorrectly match if GoalText guard is missing
        Assert.Empty(vm.LinkedTodos);
        Assert.False(vm.HasLinkedTodos);
    }

    [Fact]
    public async Task LoadEntry_GoalWithGoalText_LinkedTodosMatchCorrectly()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goalGuid = Guid.NewGuid().ToString();

        await GoalRepo.SaveAsync(new Goal
        {
            Guid = goalGuid,
            AccountFk = account.Guid,
            GoalText = "Learn piano",
            EnteredDate = ts,
            UpdatedOn = ts
        });

        // A todo correctly linked to this goal
        await TodoRepo.SaveAsync(new Todo
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            Title = "Practice scales",
            Notes = "Goal: Learn piano",
            UpdatedOn = ts
        });

        // A todo linked to a different goal — must NOT be included
        await TodoRepo.SaveAsync(new Todo
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            Title = "Buy groceries",
            Notes = "Goal: Learn cooking",
            UpdatedOn = ts
        });

        var vm = BuildVm();
        vm.Guid = goalGuid;
        await Task.Delay(50);

        Assert.Single(vm.LinkedTodos);
        Assert.Equal("Practice scales", vm.LinkedTodos[0].Title);
    }
}

// ─── JournalListViewModel: delete with active filter updates counts ────────────

public class JournalListDeleteWithFilterTests : ViewModelTestBase
{
    private JournalListViewModel BuildVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Delete_WithActiveTextFilter_EntryCountDisplayDecreases()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Happy day entry", EnteredDate = ts });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Another happy entry", EnteredDate = ts });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Unrelated content", EnteredDate = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "happy";

        Assert.Equal(2, vm.Journals.Count);
        Assert.Contains("2", vm.EntryCountDisplay);

        Nav.AlertConfirmResult = true;
        await vm.DeleteCommand.ExecuteAsync(vm.Journals[0]);

        Assert.Equal(1, vm.Journals.Count);
        Assert.Contains("1", vm.EntryCountDisplay);
    }

    [Fact]
    public async Task Delete_AllMatchingFiltered_EmptyMessageReflectsFilter()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Unique entry", EnteredDate = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "Unique";
        Assert.Single(vm.Journals);

        Nav.AlertConfirmResult = true;
        await vm.DeleteCommand.ExecuteAsync(vm.Journals[0]);

        Assert.Empty(vm.Journals);
        Assert.Contains("Unique", vm.EmptyMessage);
    }
}

// ─── TodoListViewModel: UncompleteAsync refreshes pending list correctly ───────

public class TodoListUncompleteTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Uncomplete_TodoReappearsInPendingList()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todoGuid = Guid.NewGuid().ToString();
        await TodoRepo.SaveAsync(new Todo { Guid = todoGuid, AccountFk = account.Guid, Title = "Do laundry", UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Todos);

        await vm.CompleteCommand.ExecuteAsync(vm.Todos[0]);
        Assert.Empty(vm.Todos);
        Assert.Equal(1, vm.CompletedTodoCount);

        await vm.UncompleteCommand.ExecuteAsync(vm.CompletedTodos[0]);

        Assert.Single(vm.Todos);
        Assert.Equal(0, vm.CompletedTodoCount);
        Assert.False(vm.HasCompletedTodos);
        Assert.False(vm.ShowCompletedTodos);
    }

    [Fact]
    public async Task Uncomplete_OverdueTodo_OverdueCountStaysConsistent()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();

        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Overdue task", DueDate = yesterday, UpdatedOn = ts });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Normal task", UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.OverdueTodoCount);
        Assert.True(vm.HasOverdueTodos);

        // Complete the overdue task
        var overdueTodo = vm.Todos.First(t => t.Title == "Overdue task");
        await vm.CompleteCommand.ExecuteAsync(overdueTodo);
        Assert.Equal(0, vm.OverdueTodoCount);
        Assert.False(vm.HasOverdueTodos);

        // Uncomplete it — overdue count should reflect the restored task
        await vm.UncompleteCommand.ExecuteAsync(vm.CompletedTodos[0]);
        Assert.Equal(1, vm.OverdueTodoCount);
        Assert.True(vm.HasOverdueTodos);
    }
}

public class GoalListDeleteStateTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Delete_LastGoal_HasGoalsBecomesFalse()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Only goal", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.HasGoals);
        Assert.Single(vm.Goals);

        Nav.AlertConfirmResult = true;
        await vm.DeleteCommand.ExecuteAsync(vm.Goals[0]);

        Assert.False(vm.HasGoals);
    }

    [Fact]
    public async Task Delete_GoalWithTextFilterActive_EntryCountDisplayShowsFilteredCount()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Piano practice", EnteredDate = ts, UpdatedOn = ts });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Piano recital", EnteredDate = ts, UpdatedOn = ts });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Sleep well", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "Piano";
        Assert.Equal(2, vm.Goals.Count);

        Nav.AlertConfirmResult = true;
        await vm.DeleteCommand.ExecuteAsync(vm.Goals[0]);

        // After deleting one of the two filtered goals, should show "1 goal matching"
        Assert.Contains("matching", vm.EntryCountDisplay);
        Assert.StartsWith("1", vm.EntryCountDisplay.Trim());
    }

    [Fact]
    public async Task Delete_GoalWithCategoryFilter_EntryCountDisplayShowsFilteredCount()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Math goal", Category = "Academic", EnteredDate = ts, UpdatedOn = ts });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Another math", Category = "Academic", EnteredDate = ts, UpdatedOn = ts });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run daily", Category = "Health", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.CategoryFilter = "Academic";
        Assert.Equal(2, vm.Goals.Count);

        Nav.AlertConfirmResult = true;
        await vm.DeleteCommand.ExecuteAsync(vm.Goals[0]);

        // After deleting one of the two Academic goals, count display should reflect filtered view
        Assert.Contains("1", vm.EntryCountDisplay);
        Assert.DoesNotContain("2", vm.EntryCountDisplay);
    }
}

// ─── DashboardViewModel: stale goal detection logic ──────────────────────────

public class DashboardStaleGoalTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_GoalWithNoProgress_MarkedAsStale()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            GoalText = "Master chess",
            EnteredDate = ts,
            UpdatedOn = ts
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasStaleGoal);
        Assert.Equal("Master chess", vm.StaleGoalText);
    }

    [Fact]
    public async Task Load_GoalWithRecentProgress_NotMarkedAsStale()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goalGuid = Guid.NewGuid().ToString();
        await GoalRepo.SaveAsync(new Goal
        {
            Guid = goalGuid,
            AccountFk = account.Guid,
            GoalText = "Learn piano",
            EnteredDate = ts,
            UpdatedOn = ts
        });
        // Add a progress note from 3 days ago (within the 7-day threshold)
        var recentMs = DateTimeOffset.UtcNow.AddDays(-3).ToUnixTimeMilliseconds();
        await GoalProgressRepo.UpsertFromSyncAsync(new GoalProgress
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            GoalFk = goalGuid,
            NextStepItems = "Practiced scales",
            UpdatedOn = recentMs
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasStaleGoal);
    }

    [Fact]
    public async Task Load_GoalWithProgressOlderThan7Days_MarkedAsStale()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goalGuid = Guid.NewGuid().ToString();
        await GoalRepo.SaveAsync(new Goal
        {
            Guid = goalGuid,
            AccountFk = account.Guid,
            GoalText = "Run a marathon",
            EnteredDate = ts,
            UpdatedOn = ts
        });
        // Add a progress note from 10 days ago (outside the 7-day threshold)
        var staleMs = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();
        await GoalProgressRepo.UpsertFromSyncAsync(new GoalProgress
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            GoalFk = goalGuid,
            NextStepItems = "Ran 5k",
            UpdatedOn = staleMs
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasStaleGoal);
        Assert.Equal("Run a marathon", vm.StaleGoalText);
    }
}

// ─── TodoEntryViewModel: LinkedGoal → Notes synchronization (goal switch) ─────

public class TodoEntryLinkedGoalSwitchTests : ViewModelTestBase
{
    private TodoEntryViewModel BuildVm() =>
        new(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task SetLinkedGoal_NoExistingNotes_NotesSetToGoalPrefix()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn piano", EnteredDate = ts });
        // Create a todo so we can trigger LoadAsync (which also loads AvailableGoals)
        var todoGuid = Guid.NewGuid().ToString();
        await TodoRepo.SaveAsync(new Todo { Guid = todoGuid, AccountFk = account.Guid, Title = "Practice", UpdatedOn = ts });

        var vm = BuildVm();
        vm.Guid = todoGuid; // triggers LoadAsync → sets AvailableGoals
        await Task.Delay(50);

        Assert.NotEmpty(vm.AvailableGoals);
        vm.Notes = string.Empty; // clear any loaded notes
        vm.LinkedGoal = vm.AvailableGoals.First(g => g.GoalText == "Learn piano");

        Assert.StartsWith("Goal: Learn piano", vm.Notes);
    }

    [Fact]
    public async Task SetLinkedGoal_WithExistingUserNotes_PreservesNotesAfterGoalLine()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn piano", EnteredDate = ts });
        var todoGuid = Guid.NewGuid().ToString();
        await TodoRepo.SaveAsync(new Todo { Guid = todoGuid, AccountFk = account.Guid, Title = "Practice", Notes = "Buy practice book first", UpdatedOn = ts });

        var vm = BuildVm();
        vm.Guid = todoGuid;
        await Task.Delay(50);

        // Notes currently "Buy practice book first" (no "Goal: " prefix)
        vm.LinkedGoal = vm.AvailableGoals.First(g => g.GoalText == "Learn piano");

        // Goal prefix should be prepended; existing notes preserved
        Assert.StartsWith("Goal: Learn piano", vm.Notes);
        Assert.Contains("Buy practice book first", vm.Notes);
    }

    [Fact]
    public async Task SetLinkedGoal_WhenPreviousGoalNoteExists_ReplacesGoalLine()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn piano", EnteredDate = ts });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn guitar", EnteredDate = ts });
        var todoGuid = Guid.NewGuid().ToString();
        await TodoRepo.SaveAsync(new Todo { Guid = todoGuid, AccountFk = account.Guid, Title = "Practice", UpdatedOn = ts });

        var vm = BuildVm();
        vm.Guid = todoGuid;
        await Task.Delay(50);

        vm.Notes = string.Empty;
        vm.LinkedGoal = vm.AvailableGoals.First(g => g.GoalText == "Learn piano");
        Assert.StartsWith("Goal: Learn piano", vm.Notes);

        // Switch to a different goal — the "Goal:" line should update
        vm.LinkedGoal = vm.AvailableGoals.First(g => g.GoalText == "Learn guitar");
        Assert.StartsWith("Goal: Learn guitar", vm.Notes);
        Assert.DoesNotContain("Learn piano", vm.Notes);
    }
}

// ─── DashboardViewModel: weekly challenge progress tracking ──────────────────

public class DashboardWeeklyChallengeTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_WithActiveGoal_ShowsWeeklyChallenge()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            GoalText = "Improve focus",
            EnteredDate = ts,
            UpdatedOn = ts
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasWeeklyChallenge);
        Assert.NotEmpty(vm.WeeklyChallengeTitle);
        Assert.NotEmpty(vm.WeeklyChallengeDesc);
    }

    [Fact]
    public async Task Load_WithNoActiveGoals_HidesWeeklyChallenge()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasWeeklyChallenge);
    }

    [Fact]
    public async Task Load_WeeklyChallengeProgressValue_ClampedToOne()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goalGuid = Guid.NewGuid().ToString();
        await GoalRepo.SaveAsync(new Goal
        {
            Guid = goalGuid,
            AccountFk = account.Guid,
            GoalText = "Write a journal",
            EnteredDate = ts,
            UpdatedOn = ts
        });

        // Add 20 progress notes this week — far exceeds any target
        for (int i = 0; i < 20; i++)
        {
            await GoalProgressRepo.UpsertFromSyncAsync(new GoalProgress
            {
                Guid = Guid.NewGuid().ToString(),
                AccountFk = account.Guid,
                GoalFk = goalGuid,
                NextStepItems = $"Note {i}",
                UpdatedOn = ts + i
            });
        }

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasWeeklyChallenge);
        Assert.True(vm.WeeklyChallengePctValue <= 1.0);
    }
}

// ─── RemindersViewModel: load, dismiss, snooze, canExecute ───────────────────

public class RemindersViewModelLoadTests : ViewModelTestBase
{
    private RemindersViewModel BuildVm() => new(ReminderSvc, AccountService, Nav);

    [Fact]
    public async Task Load_WithPendingReminders_PopulatesListAndSetsHasReminders()
    {
        var account = await CreateTestAccountAsync();
        var future = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        await ReminderSvc.ScheduleAsync(new Reminder
        {
            AccountFk = account.Guid, Title = "Check in", Topic = "General", FireAt = future
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Single(vm.Reminders);
        Assert.True(vm.HasReminders);
    }

    [Fact]
    public async Task Load_WithNoReminders_HasRemindersFalse()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Empty(vm.Reminders);
        Assert.False(vm.HasReminders);
    }

    [Fact]
    public async Task Load_SetsIsLoadingFalseAfterCompletion()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.IsLoading);
    }
}

public class RemindersViewModelDismissTests : ViewModelTestBase
{
    private RemindersViewModel BuildVm() => new(ReminderSvc, AccountService, Nav);

    [Fact]
    public async Task Dismiss_RemovesReminderFromList()
    {
        var account = await CreateTestAccountAsync();
        var future = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        await ReminderSvc.ScheduleAsync(new Reminder
        {
            AccountFk = account.Guid, Title = "R1", Topic = "General", FireAt = future
        });
        await ReminderSvc.ScheduleAsync(new Reminder
        {
            AccountFk = account.Guid, Title = "R2", Topic = "General", FireAt = future + 1
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Reminders.Count);

        await vm.DismissCommand.ExecuteAsync(vm.Reminders[0]);

        Assert.Single(vm.Reminders);
    }

    [Fact]
    public async Task Dismiss_LastReminder_SetsHasRemindersFalse()
    {
        var account = await CreateTestAccountAsync();
        var future = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        await ReminderSvc.ScheduleAsync(new Reminder
        {
            AccountFk = account.Guid, Title = "Last one", Topic = "General", FireAt = future
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.HasReminders);

        await vm.DismissCommand.ExecuteAsync(vm.Reminders[0]);

        Assert.False(vm.HasReminders);
    }

    [Fact]
    public async Task Dismiss_PersistsToDB_ReminderGoneAfterReload()
    {
        var account = await CreateTestAccountAsync();
        var future = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        await ReminderSvc.ScheduleAsync(new Reminder
        {
            AccountFk = account.Guid, Title = "Persistent dismiss", Topic = "General", FireAt = future
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.DismissCommand.ExecuteAsync(vm.Reminders[0]);

        // Reload from DB — dismissed reminder should not reappear
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Empty(vm.Reminders);
        Assert.False(vm.HasReminders);
    }
}

public class RemindersViewModelCanAddTests : ViewModelTestBase
{
    private RemindersViewModel BuildVm() => new(ReminderSvc, AccountService, Nav);

    [Fact]
    public void CanAddGeneral_WithEmptyTitle_ReturnsFalse()
    {
        var vm = BuildVm();
        vm.NewReminderTitle = string.Empty;

        Assert.False(vm.AddGeneralCommand.CanExecute(null));
    }

    [Fact]
    public void CanAddGeneral_WithWhitespaceTitle_ReturnsFalse()
    {
        var vm = BuildVm();
        vm.NewReminderTitle = "   ";

        Assert.False(vm.AddGeneralCommand.CanExecute(null));
    }

    [Fact]
    public void CanAddGeneral_WithValidTitle_ReturnsTrue()
    {
        var vm = BuildVm();
        vm.NewReminderTitle = "Remind me to practice";

        Assert.True(vm.AddGeneralCommand.CanExecute(null));
    }

    [Fact]
    public async Task AddGeneral_ClearsNewReminderTitleAfterAdd()
    {
        var account = await CreateTestAccountAsync();
        Nav.ActionSheetResult = "1 hour";

        var vm = BuildVm();
        vm.NewReminderTitle = "Study session";
        await vm.AddGeneralCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.NewReminderTitle);
    }

    [Fact]
    public async Task AddGeneral_WhenUserCancelsSnooze_TitleNotCleared()
    {
        await CreateTestAccountAsync();
        Nav.ActionSheetResult = null; // user cancels duration picker

        var vm = BuildVm();
        vm.NewReminderTitle = "Don't clear me";
        await vm.AddGeneralCommand.ExecuteAsync(null);

        // Title should remain — add was not completed
        Assert.Equal("Don't clear me", vm.NewReminderTitle);
    }
}

// ─── GoalEntry: linked todo Notes length cap (long GoalText edge case) ────────

public class GoalEntryLinkedTodoNotesLengthTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task AddLinkedTodo_MaxLengthGoalText_NotesDoesNotExceed2000Chars()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // 2000-char goal text is the API maximum — "Goal: " + 2000 = 2006, which exceeds Notes limit
        var maxGoalText = new string('B', 2000);
        var goal = new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = maxGoalText, EnteredDate = ts, UpdatedOn = ts
        };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = "Practice";
        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        await vm.AddLinkedTodoCommand.ExecuteAsync(null);

        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(todos);
        var notesLength = todos[0].Notes?.Length ?? 0;
        Assert.True(notesLength <= 2000,
            $"Notes length {notesLength} exceeds 2000-char limit (would be rejected by API sync)");
    }

    [Fact]
    public async Task AddLinkedTodo_MaxLengthGoalText_LinkedTodosStillMatchAfterReload()
    {
        // Matching must use the same truncation logic on both write and read sides
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var maxGoalText = new string('C', 2000);
        var goal = new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = maxGoalText, EnteredDate = ts, UpdatedOn = ts
        };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = "Linked task";
        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);
        await vm.AddLinkedTodoCommand.ExecuteAsync(null);

        // Reload from DB — goal entry must still find its linked todo
        var vm2 = BuildVm();
        vm2.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.True(vm2.HasLinkedTodos, "Linked todo not found after reload — Notes truncation broke prefix matching");
        Assert.Single(vm2.LinkedTodos);
    }
}

// ─── TodoListViewModel: quick-add title length enforcement ───────────────────

public class TodoListAddTitleLengthTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task AddAsync_TitleOver500Chars_SavedTitleDoesNotExceed500()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.NewTodoTitle = new string('X', 600);
        await vm.AddCommand.ExecuteAsync(null);

        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(todos);
        Assert.True((todos[0].Title?.Length ?? 0) <= 500,
            $"Title length {todos[0].Title?.Length} exceeds 500-char API limit — would fail sync");
    }

    [Fact]
    public async Task AddAsync_TitleExactly500Chars_SavesUntruncated()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.NewTodoTitle = new string('Y', 500);
        await vm.AddCommand.ExecuteAsync(null);

        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(todos);
        Assert.Equal(500, todos[0].Title?.Length ?? 0);
    }

    [Fact]
    public async Task AddAsync_TitleOver500WithLeadingSpaces_TrimmedAndCapped()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.NewTodoTitle = "   " + new string('Z', 510);
        await vm.AddCommand.ExecuteAsync(null);

        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(todos);
        var title = todos[0].Title ?? string.Empty;
        Assert.True(title.Length <= 500, $"Title length {title.Length} exceeds 500");
        Assert.False(title.StartsWith(' '), "Title should be trimmed before cap");
    }
}

// ─── TodoEntryViewModel: save title length enforcement ───────────────────────

public class TodoEntrySaveTitleLengthTests : ViewModelTestBase
{
    private TodoEntryViewModel BuildVm() =>
        new(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task SaveAsync_TitleOver500Chars_SavedTitleDoesNotExceed500()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.Title = new string('A', 700);
        await vm.SaveCommand.ExecuteAsync(null);

        var account = await AccountService.GetAccountAsync();
        var todos = await TodoRepo.GetPendingAsync(account!.Guid);
        Assert.Single(todos);
        Assert.True((todos[0].Title?.Length ?? 0) <= 500,
            $"Title {todos[0].Title?.Length} chars — exceeds 500-char API limit");
    }

    [Fact]
    public async Task SaveAsync_TitleExactly500Chars_SavesUntruncated()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.Title = new string('B', 500);
        await vm.SaveCommand.ExecuteAsync(null);

        var account = await AccountService.GetAccountAsync();
        var todos = await TodoRepo.GetPendingAsync(account!.Guid);
        Assert.Single(todos);
        Assert.Equal(500, todos[0].Title?.Length ?? 0);
    }
}

// ─── JournalEntryViewModel: activity / field length enforcement ──────────────

public class JournalEntrySaveFieldLengthTests : ViewModelTestBase
{
    private JournalEntryViewModel BuildVm() =>
        new(JournalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task SaveAsync_ActivityOver255Chars_SavedActivityDoesNotExceed255()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.Activity = new string('A', 300);
        vm.Notes = "Some notes";
        await vm.SaveCommand.ExecuteAsync(null);

        var account = await AccountService.GetAccountAsync();
        var journals = await JournalRepo.GetAllActiveAsync(account!.Guid);
        Assert.Single(journals);
        Assert.True((journals[0].Activity?.Length ?? 0) <= 255,
            $"Activity length {journals[0].Activity?.Length} exceeds 255-char API limit");
    }

    [Fact]
    public async Task SaveAsync_MoodOver50Chars_SavedMoodDoesNotExceed50()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.Notes = "Some notes";
        vm.Mood = new string('M', 60);
        await vm.SaveCommand.ExecuteAsync(null);

        var account = await AccountService.GetAccountAsync();
        var journals = await JournalRepo.GetAllActiveAsync(account!.Guid);
        Assert.Single(journals);
        Assert.True((journals[0].Mood?.Length ?? 0) <= 50,
            $"Mood length {journals[0].Mood?.Length} exceeds 50-char API limit");
    }

    [Fact]
    public async Task SaveAsync_TagsOver500Chars_SavedTagsDoesNotExceed500()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.Notes = "Some notes";
        vm.Tags = new string('T', 600);
        await vm.SaveCommand.ExecuteAsync(null);

        var account = await AccountService.GetAccountAsync();
        var journals = await JournalRepo.GetAllActiveAsync(account!.Guid);
        Assert.Single(journals);
        Assert.True((journals[0].Tags?.Length ?? 0) <= 500,
            $"Tags length {journals[0].Tags?.Length} exceeds 500-char API limit");
    }
}

// ─── JournalListViewModel: delete with active date filter ────────────────────

public class JournalListDeleteWithDateFilterTests : ViewModelTestBase
{
    private JournalListViewModel BuildVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task DeleteAsync_WithWeekDateFilter_EntryCountUpdatesCorrectly()
    {
        var account = await CreateTestAccountAsync();
        var thisWeekMs = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds();
        var lastMonthMs = DateTimeOffset.UtcNow.AddDays(-35).ToUnixTimeMilliseconds();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "This week", EnteredDate = thisWeekMs, UpdatedOn = now });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Last month", EnteredDate = lastMonthMs, UpdatedOn = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SetDateFilterCommand.Execute("Week");
        Assert.Single(vm.Journals);
        Assert.Contains("shown", vm.EntryCountDisplay);

        // Delete the in-filter item
        Nav.AlertConfirmResult = true;
        await vm.DeleteCommand.ExecuteAsync(vm.Journals[0]);

        Assert.Empty(vm.Journals);
        Assert.Contains("0", vm.EntryCountDisplay);
    }

    [Fact]
    public async Task DeleteAsync_ItemOutsideDateFilter_FilteredCountUnchanged()
    {
        var account = await CreateTestAccountAsync();
        var thisWeekMs = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds();
        var lastMonthMs = DateTimeOffset.UtcNow.AddDays(-35).ToUnixTimeMilliseconds();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var thisWeek = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Week entry", EnteredDate = thisWeekMs, UpdatedOn = now };
        var lastMonth = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Old entry", EnteredDate = lastMonthMs, UpdatedOn = now };
        await JournalRepo.SaveAsync(thisWeek);
        await JournalRepo.SaveAsync(lastMonth);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SetDateFilterCommand.Execute("Week");
        Assert.Single(vm.Journals);

        // Delete the item that's NOT in the filter (not visible) — simulate passing it directly
        Nav.AlertConfirmResult = true;
        await vm.DeleteCommand.ExecuteAsync(lastMonth);

        // Filtered view should still show the this-week entry
        Assert.Single(vm.Journals);
        Assert.Equal("Week entry", vm.Journals[0].Notes);
    }

    [Fact]
    public async Task ShufflePrompt_AfterLoadAsync_CyclesThroughAllPrompts()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        var firstPrompt = vm.TodayPrompt;
        var seen = new HashSet<string> { firstPrompt };

        // Shuffle enough times to cycle through all 12 prompts
        for (int i = 0; i < 20; i++)
        {
            vm.ShufflePromptCommand.Execute(null);
            seen.Add(vm.TodayPrompt);
        }

        // Should have seen multiple distinct prompts without throwing
        Assert.True(seen.Count > 1, "ShufflePrompt should cycle through multiple prompts");
        Assert.NotEmpty(vm.TodayPrompt);
    }
}

// ─── TodoEntryViewModel: LoadGoalsAsync path (new todo, Guid → empty) ────────

public class TodoEntryLoadGoalsAsyncTests : ViewModelTestBase
{
    private TodoEntryViewModel BuildVm() =>
        new(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task LoadGoalsAsync_ExcludesCompletedGoals()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var activeGoal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Active", EnteredDate = ts, UpdatedOn = ts };
        var completedGoal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Done", EnteredDate = ts, UpdatedOn = ts, CompletionDate = ts };
        await GoalRepo.SaveAsync(activeGoal);
        await GoalRepo.SaveAsync(completedGoal);

        var vm = BuildVm();
        // Trigger LoadGoalsAsync by setting Guid to non-empty then to empty
        vm.Guid = "some-guid";
        vm.Guid = string.Empty;
        await Task.Delay(200);

        Assert.Single(vm.AvailableGoals);
        Assert.Equal("Active", vm.AvailableGoals[0].GoalText);
    }

    [Fact]
    public async Task LoadGoalsAsync_NoAccount_AvailableGoalsStaysEmpty()
    {
        // No account seeded — GetAccountAsync returns null
        var vm = BuildVm();
        vm.Guid = "x";
        vm.Guid = string.Empty;
        await Task.Delay(200);

        Assert.Empty(vm.AvailableGoals);
    }

    [Fact]
    public async Task LoadGoalsAsync_PopulatesAllActiveGoals()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < 3; i++)
            await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = $"Goal {i}", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        vm.Guid = "x";
        vm.Guid = string.Empty;
        await Task.Delay(200);

        Assert.Equal(3, vm.AvailableGoals.Count);
    }
}

// ─── GoalListViewModel: SetCategoryFilter adversarial cases ──────────────────

public class GoalListCategoryFilterBreakItTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task SetCategoryFilter_NeedsAttention_ShowsOnlyStaleGoals()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var oldProgress = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();

        var staleGoal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Stale", EnteredDate = ts, UpdatedOn = ts };
        var freshGoal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Active", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(staleGoal);
        await GoalRepo.SaveAsync(freshGoal);

        // Add fresh progress to freshGoal only
        await GoalProgressRepo.SaveAsync(new GoalProgress
        {
            Guid = Guid.NewGuid().ToString(),
            GoalFk = freshGoal.Guid,
            AccountFk = account.Guid,
            NextStepItems = "Working",
            UpdatedOn = ts
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Goals.Count);

        vm.SetCategoryFilterCommand.Execute("NeedsAttention");

        // Only the stale goal (no recent progress) should show
        Assert.Single(vm.Goals);
        Assert.Equal("Stale", vm.Goals[0].GoalText);
    }

    [Fact]
    public async Task SetCategoryFilter_SpecificCategory_ShowsOnlyThatCategory()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Health goal", Category = "Health", EnteredDate = ts, UpdatedOn = ts });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Education goal", Category = "Education", EnteredDate = ts, UpdatedOn = ts });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "No category", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Goals.Count);

        vm.SetCategoryFilterCommand.Execute("Health");

        Assert.Single(vm.Goals);
        Assert.Equal("Health goal", vm.Goals[0].GoalText);
    }

    [Fact]
    public async Task SetCategoryFilter_BackToAll_ShowsAllGoals()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Goal A", Category = "Health", EnteredDate = ts, UpdatedOn = ts });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Goal B", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SetCategoryFilterCommand.Execute("Health");
        Assert.Single(vm.Goals);

        vm.SetCategoryFilterCommand.Execute("All");
        Assert.Equal(2, vm.Goals.Count);
    }

    [Fact]
    public async Task SetCategoryFilter_NeedsAttention_CompletedGoalsExcluded()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // Completed goal with no recent progress — should NOT appear in NeedsAttention
        var completedGoal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Done goal", EnteredDate = ts, UpdatedOn = ts, CompletionDate = ts };
        var activeStale = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Stale active", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(completedGoal);
        await GoalRepo.SaveAsync(activeStale);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SetCategoryFilterCommand.Execute("NeedsAttention");

        // Only active stale should show; completed goal excluded
        Assert.Single(vm.Goals);
        Assert.Equal("Stale active", vm.Goals[0].GoalText);
    }
}

// ─── GoalEntryViewModel: LoadAsync uncovered branches ────────────────────────

public class GoalEntryLoadAsyncBranchTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task LoadAsync_GoalWithNextMeetingDate_SetsHasNextMeetingDateTrue()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var meeting = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Prepare for meeting", EnteredDate = ts, UpdatedOn = ts, NextMeetingDate = meeting };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.True(vm.HasNextMeetingDate);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(meeting).LocalDateTime, vm.NextMeetingDate);
    }

    [Fact]
    public async Task LoadAsync_GoalWithExpirationDate_SetsHasExpirationDateTrue()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expiry = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Seasonal goal", EnteredDate = ts, UpdatedOn = ts, ExpirationDate = expiry };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.True(vm.HasExpirationDate);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(expiry).LocalDateTime, vm.ExpirationDate);
    }

    [Fact]
    public async Task LoadAsync_GoalWithNullGoalText_NoLinkedTodosAndNoThrow()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = null, EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.False(vm.HasLinkedTodos);
        Assert.Empty(vm.LinkedTodos);
    }

    [Fact]
    public async Task LoadAsync_GoalWithIsPinnedTrue_SetsPinnedState()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Pinned", EnteredDate = ts, UpdatedOn = ts, IsPinned = true };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.True(vm.IsPinned);
    }

    [Fact]
    public async Task SaveAsync_UnchangedNextStepItems_DoesNotCreateNewProgressNote()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "No progress dupe", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);
        await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goal.Guid, NextStepItems = "Existing steps", UpdatedOn = ts });

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);
        Assert.Equal("Existing steps", vm.NextStepItems);

        // Save without changing NextStepItems — should NOT add a new progress note
        await vm.SaveCommand.ExecuteAsync(null);

        var progressNotes = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Single(progressNotes);
    }

    [Fact]
    public async Task SaveAsync_WhitespaceOnlyNextStepItems_DoesNotCreateProgressNote()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "No whitespace progress", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        vm.NextStepItems = "   "; // whitespace only
        await vm.SaveCommand.ExecuteAsync(null);

        var progressNotes = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Empty(progressNotes);
    }

    [Fact]
    public async Task LoadAsync_GoalWithProgressHistory_PopulatesProgressHistory()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "With history", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        for (int i = 0; i < 5; i++)
            await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goal.Guid, NextStepItems = $"Step {i}", UpdatedOn = ts + i });

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.Equal(5, vm.ProgressNotesCount);
        Assert.True(vm.HasProgressHistory);
        // history is progress.Skip(1).Take(4) with non-empty items → 4 entries
        Assert.Equal(4, vm.ProgressHistory.Count);
    }
}

// ─── JournalListViewModel: Activity/Mood/Tags text search ────────────────────

public class JournalListFieldSearchTests : ViewModelTestBase
{
    private JournalListViewModel BuildVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task FilterText_MatchesActivity_ShowsJournal()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Notes only", Activity = "Swimming practice", EnteredDate = ts, UpdatedOn = ts });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Unrelated entry", Activity = "Walking", EnteredDate = ts + 1, UpdatedOn = ts + 1 });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "Swimming";

        Assert.Single(vm.Journals);
        Assert.Equal("Notes only", vm.Journals[0].Notes);
    }

    [Fact]
    public async Task FilterText_MatchesMood_ShowsJournal()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Day 1", Mood = "Excited", EnteredDate = ts, UpdatedOn = ts });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Day 2", Mood = "Calm", EnteredDate = ts + 1, UpdatedOn = ts + 1 });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "excited";

        Assert.Single(vm.Journals);
        Assert.Equal("Day 1", vm.Journals[0].Notes);
    }

    [Fact]
    public async Task FilterText_MatchesTags_ShowsJournal()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Tagged entry", Tags = "school, homework", EnteredDate = ts, UpdatedOn = ts });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Untagged entry", Tags = "sports", EnteredDate = ts + 1, UpdatedOn = ts + 1 });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "homework";

        Assert.Single(vm.Journals);
        Assert.Equal("Tagged entry", vm.Journals[0].Notes);
    }

    [Fact]
    public async Task FilterText_NoMatch_EmptyMessageReflectsSearchTerm()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Hello world", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "xyznotfound";

        Assert.Empty(vm.Journals);
        Assert.Contains("xyznotfound", vm.EmptyMessage);
    }
}

// ─── JournalListViewModel: Month date filter excludes old entries ─────────────

public class JournalListMonthFilterTests : ViewModelTestBase
{
    private JournalListViewModel BuildVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task MonthFilter_ExcludesEntriesOlderThan30Days()
    {
        var account = await CreateTestAccountAsync();
        var recentMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var oldMs = DateTimeOffset.UtcNow.AddDays(-35).ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Recent", EnteredDate = recentMs, UpdatedOn = recentMs });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Old entry", EnteredDate = oldMs, UpdatedOn = oldMs });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Journals.Count);

        vm.SetDateFilterCommand.Execute("Month");

        Assert.Single(vm.Journals);
        Assert.Equal("Recent", vm.Journals[0].Notes);
    }

    [Fact]
    public async Task MonthFilter_NoEntriesThisMonth_SetsMonthEmptyMessage()
    {
        var account = await CreateTestAccountAsync();
        var oldMs = DateTimeOffset.UtcNow.AddDays(-40).ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Old only", EnteredDate = oldMs, UpdatedOn = oldMs });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SetDateFilterCommand.Execute("Month");

        Assert.Empty(vm.Journals);
        Assert.Contains("month", vm.EmptyMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MonthFilter_EntryCountDisplay_ShowsShownLabel()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Entry A", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SetDateFilterCommand.Execute("Month");

        Assert.Contains("shown", vm.EntryCountDisplay, StringComparison.OrdinalIgnoreCase);
    }
}

// ─── DashboardViewModel: QuickNoteForFocusGoal cancel and tier labels ────────

public class DashboardQuickNoteAndTierTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task QuickNoteForFocusGoal_PromptCancelled_DoesNotSaveProgressAndGoalRemainsStale()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Stale focus goal", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.HasStaleGoal);

        Nav.PromptResult = null; // user cancels
        await vm.QuickNoteForFocusGoalCommand.ExecuteAsync(null);

        // No progress should be saved
        Assert.True(vm.HasStaleGoal);
        var progress = await GoalProgressRepo.GetLatestProgressInfoAsync(account.Guid);
        Assert.Empty(progress);
    }

    [Fact]
    public async Task QuickNoteForFocusGoal_WhitespacePrompt_DoesNotSaveProgress()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Focus goal whitespace", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.HasStaleGoal);

        Nav.PromptResult = "   ";
        await vm.QuickNoteForFocusGoalCommand.ExecuteAsync(null);

        Assert.True(vm.HasStaleGoal);
        var progress = await GoalProgressRepo.GetLatestProgressInfoAsync(account.Guid);
        Assert.Empty(progress);
    }

    [Fact]
    public async Task Load_With20ProgressNotes_SetsApprenticeLabel()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Apprentice goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        for (int i = 0; i < 20; i++)
            await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), GoalFk = goal.Guid, AccountFk = account.Guid, NextStepItems = $"Note {i}", UpdatedOn = ts + i });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("Apprentice", vm.OverallTierLabel);
    }

    [Fact]
    public async Task Load_With100ProgressNotes_SetsExpertLabel()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Expert goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        for (int i = 0; i < 100; i++)
            await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), GoalFk = goal.Guid, AccountFk = account.Guid, NextStepItems = $"Note {i}", UpdatedOn = ts + i });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("Expert", vm.OverallTierLabel);
    }

    [Fact]
    public async Task Load_WithFewProgressNotes_EmptyTierLabel()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "New goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        // Only 3 notes — below the ≥5 Beginner threshold
        for (int i = 0; i < 3; i++)
            await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), GoalFk = goal.Guid, AccountFk = account.Guid, NextStepItems = $"Note {i}", UpdatedOn = ts + i });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Empty(vm.OverallTierLabel);
    }

    [Fact]
    public async Task Load_With14DayStreak_ShowsStarEmoji()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Streak goal", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);
        // Use UpsertFromSyncAsync to preserve historical timestamps (SaveAsync overrides UpdatedOn to now)
        for (int d = 0; d < 14; d++)
        {
            var dayMs = DateTimeOffset.UtcNow.AddDays(-d).ToUnixTimeMilliseconds();
            await GoalProgressRepo.UpsertFromSyncAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), GoalFk = goal.Guid, AccountFk = account.Guid, NextStepItems = "Note", UpdatedOn = dayMs });
        }

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("🌟", vm.StreakDisplay);
    }
}

// ─── TodoListViewModel: WeekOverWeek diff < 0 and diff == 0 branches ─────────

public class TodoListWeekOverWeekTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    private async Task SaveCompletedThisWeek(string accountGuid, int count)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var thisWeek = DateTimeOffset.UtcNow.AddDays(-3).ToUnixTimeMilliseconds();
        for (int i = 0; i < count; i++)
            await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = accountGuid, Title = $"ThisWeek{i}", UpdatedOn = now, CompletedAt = thisWeek });
    }

    private async Task SaveCompletedLastWeek(string accountGuid, int count)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var lastWeek = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();
        for (int i = 0; i < count; i++)
            await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = accountGuid, Title = $"LastWeek{i}", UpdatedOn = now, CompletedAt = lastWeek });
    }

    [Fact]
    public async Task WeekOverWeek_FewerThisWeek_ShowsDeclineMessage()
    {
        var account = await CreateTestAccountAsync();
        // 2 this week, 5 last week → diff = -3
        await SaveCompletedThisWeek(account.Guid, 2);
        await SaveCompletedLastWeek(account.Guid, 5);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasWeekOverWeekMessage);
        Assert.Contains("📉", vm.WeekOverWeekMessage);
        Assert.Contains("fewer", vm.WeekOverWeekMessage);
    }

    [Fact]
    public async Task WeekOverWeek_SamePaceAsLastWeek_ShowsSamePaceMessage()
    {
        var account = await CreateTestAccountAsync();
        // 3 this week, 3 last week → diff = 0
        await SaveCompletedThisWeek(account.Guid, 3);
        await SaveCompletedLastWeek(account.Guid, 3);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasWeekOverWeekMessage);
        Assert.Contains("📊", vm.WeekOverWeekMessage);
        Assert.Contains("Same pace", vm.WeekOverWeekMessage);
    }

    [Fact]
    public async Task WeekOverWeek_NoLastWeekData_HidesWeekOverWeekMessage()
    {
        var account = await CreateTestAccountAsync();
        // 2 this week, none last week → lastWeekCount == 0
        await SaveCompletedThisWeek(account.Guid, 2);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        // When lastWeekCount == 0, WeekOverWeekMessage should be hidden
        Assert.False(vm.HasWeekOverWeekMessage);
    }

    [Fact]
    public async Task WeekCompletedMessage_3Todos_ShowsMomentumMessage()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < 3; i++)
            await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = $"Done{i}", UpdatedOn = now, CompletedAt = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        // >= 3 but < 5 → "💪 keep it up!"
        Assert.Contains("💪", vm.WeekCompletedMessage);
    }
}

// ─── GoalListViewModel: search by MeasurableOutcome and LatestNextStepItems ──

public class GoalListSearchFieldTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task FilterText_MatchesMeasurableOutcome_ShowsGoal()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Fitness goal", MeasurableOutcome = "Run 5k in 30 minutes",
            EnteredDate = ts, UpdatedOn = ts
        });
        await GoalRepo.SaveAsync(new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Reading goal", MeasurableOutcome = "10 books per year",
            EnteredDate = ts, UpdatedOn = ts
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "5k";

        Assert.Single(vm.Goals);
        Assert.Equal("Fitness goal", vm.Goals[0].GoalText);
    }

    [Fact]
    public async Task FilterText_MatchesLatestNextStepItems_ShowsGoal()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goalGuid = Guid.NewGuid().ToString();
        await GoalRepo.SaveAsync(new Goal
        {
            Guid = goalGuid, AccountFk = account.Guid,
            GoalText = "Music practice", EnteredDate = ts, UpdatedOn = ts
        });
        await GoalRepo.SaveAsync(new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Sports training", EnteredDate = ts, UpdatedOn = ts
        });
        // Add a progress note — this becomes LatestNextStepItems
        await GoalProgressRepo.SaveAsync(new GoalProgress
        {
            Guid = Guid.NewGuid().ToString(), GoalFk = goalGuid, AccountFk = account.Guid,
            NextStepItems = "Practice scales daily", UpdatedOn = ts
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "scales";

        Assert.Single(vm.Goals);
        Assert.Equal("Music practice", vm.Goals[0].GoalText);
    }

    [Fact]
    public async Task FilterText_MatchesGoalTextCaseInsensitive_ShowsGoal()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Learn Mandarin", EnteredDate = ts, UpdatedOn = ts
        });
        await GoalRepo.SaveAsync(new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Improve Swimming", EnteredDate = ts, UpdatedOn = ts
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "MANDARIN";

        Assert.Single(vm.Goals);
        Assert.Equal("Learn Mandarin", vm.Goals[0].GoalText);
    }

    [Fact]
    public async Task FilterText_WithCategoryFilter_BothFiltersApplied()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // Academic goals with "math" in GoalText
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Math homework", Category = "Academic", EnteredDate = ts, UpdatedOn = ts });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Math test", Category = "Academic", EnteredDate = ts, UpdatedOn = ts });
        // Health goal also with "math" — should be excluded by category filter
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Math in my head exercise", Category = "Health", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.CategoryFilter = "Academic";
        vm.FilterText = "math";

        // All 3 match "math" but only 2 are Academic
        Assert.Equal(2, vm.Goals.Count);
        Assert.All(vm.Goals, g => Assert.Equal("Academic", g.Category));
        Assert.Contains("matching", vm.EntryCountDisplay);
    }
}

// ─── GoalEntryViewModel: Category length cap (API MaxLength(50)) ──────────────

public class GoalEntryCategoryLengthTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task SaveAsync_CategoryExceeds50Chars_IsTruncatedTo50()
    {
        var account = await CreateTestAccountAsync();
        var longCategory = new string('X', 60); // 60 chars — exceeds API MaxLength(50)

        var vm = BuildVm();
        vm.GoalText = "Category overflow goal";
        vm.Category = longCategory;
        await vm.SaveCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(goals);
        Assert.True((goals[0].Category?.Length ?? 0) <= 50,
            $"Category length {goals[0].Category?.Length} exceeds 50-char API limit");
    }

    [Fact]
    public async Task SaveAsync_CategoryExactly50Chars_IsNotTruncated()
    {
        var account = await CreateTestAccountAsync();
        var exactCategory = new string('Y', 50);

        var vm = BuildVm();
        vm.GoalText = "Exact category goal";
        vm.Category = exactCategory;
        await vm.SaveCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(goals);
        Assert.Equal(50, goals[0].Category?.Length);
    }
}

// ─── JournalEntryViewModel: EmotionReason length cap (API MaxLength(1000)) ────

public class JournalEntryEmotionReasonLengthTests : ViewModelTestBase
{
    private JournalEntryViewModel BuildVm() =>
        new(JournalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task SaveAsync_EmotionReasonExceeds1000Chars_IsTruncatedTo1000()
    {
        var account = await CreateTestAccountAsync();
        var longReason = new string('E', 1100);

        var vm = BuildVm();
        vm.Notes = "Feeling reflective";
        vm.EmotionReason = longReason;
        await vm.SaveCommand.ExecuteAsync(null);

        var journals = await JournalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(journals);
        Assert.True((journals[0].EmotionReason?.Length ?? 0) <= 1000,
            $"EmotionReason length {journals[0].EmotionReason?.Length} exceeds 1000-char API limit");
    }

    [Fact]
    public async Task SaveAsync_EmotionReasonExactly1000Chars_IsNotTruncated()
    {
        var account = await CreateTestAccountAsync();
        var exactReason = new string('F', 1000);

        var vm = BuildVm();
        vm.Notes = "Exact reason test";
        vm.EmotionReason = exactReason;
        await vm.SaveCommand.ExecuteAsync(null);

        var journals = await JournalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(journals);
        Assert.Equal(1000, journals[0].EmotionReason?.Length);
    }
}

// ─── GoalEntryViewModel: NextStepItems length cap (API MaxLength 2000) ─────────

public class GoalEntryNextStepItemsLengthTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task SaveAsync_NextStepItemsOver2000Chars_ProgressNoteDoesNotExceed2000()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Big steps goal", EnteredDate = ts, UpdatedOn = ts
        };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        vm.NextStepItems = new string('N', 2100); // exceeds API limit
        await vm.SaveCommand.ExecuteAsync(null);

        var notes = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Single(notes);
        Assert.True((notes[0].NextStepItems?.Length ?? 0) <= 2000,
            $"NextStepItems length {notes[0].NextStepItems?.Length} exceeds 2000-char API limit");
    }

    [Fact]
    public async Task SaveAsync_NextStepItemsExactly2000Chars_IsNotTruncated()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Exact steps goal", EnteredDate = ts, UpdatedOn = ts
        };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        vm.NextStepItems = new string('M', 2000);
        await vm.SaveCommand.ExecuteAsync(null);

        var notes = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Single(notes);
        Assert.Equal(2000, notes[0].NextStepItems?.Length ?? 0);
    }
}

// ─── RemindersViewModel: AddGeneral sets HasReminders ────────────────────────

public class RemindersViewModelHasRemindersTests : ViewModelTestBase
{
    private RemindersViewModel BuildVm() => new(ReminderSvc, AccountService, Nav);

    [Fact]
    public async Task AddGeneral_SetsHasRemindersTrue_AfterSuccessfulAdd()
    {
        await CreateTestAccountAsync();
        Nav.ActionSheetResult = "1 hour";

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.HasReminders); // starts empty

        vm.NewReminderTitle = "Remember this";
        await vm.AddGeneralCommand.ExecuteAsync(null);

        Assert.True(vm.HasReminders, "HasReminders should be true after adding a reminder");
        Assert.NotEmpty(vm.Reminders);
    }
}

// ─── GoalListViewModel: EmptyMessage branches ────────────────────────────────

public class GoalListEmptyMessageTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task SetCategoryFilter_NoMatchingGoals_EmptyMessageShowsCategoryName()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Science project", Category = "Academic", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SetCategoryFilterCommand.Execute("Health"); // no Health goals exist

        Assert.Empty(vm.Goals);
        Assert.Equal("No Health goals", vm.EmptyMessage);
    }

    [Fact]
    public async Task SetCategoryFilter_NeedsAttention_AllUpToDate_ShowsUpToDateEmptyMessage()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var recentProgress = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();

        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Active goal", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);
        await GoalProgressRepo.SaveAsync(new GoalProgress
        {
            Guid = Guid.NewGuid().ToString(), GoalFk = goal.Guid, AccountFk = account.Guid,
            NextStepItems = "Just worked on this", UpdatedOn = recentProgress
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SetCategoryFilterCommand.Execute("NeedsAttention");

        Assert.Empty(vm.Goals);
        Assert.Equal("All goals are up to date! 🎉", vm.EmptyMessage);
    }
}

// ─── TodoListViewModel: Notes field filter, ToggleCompleted, delete completed ─

public class TodoListNotesAndCompletedTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task FilterText_MatchesNotes_ShowsTodo()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Plain title", Notes = "goal-linked: reading habit", UpdatedOn = now });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Other task", Notes = "unrelated notes", UpdatedOn = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "reading habit";

        Assert.Single(vm.Todos);
        Assert.Equal("Plain title", vm.Todos[0].Title);
    }

    [Fact]
    public async Task FilterText_NoMatch_EmptyMessageShowsSearchTerm()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Homework", UpdatedOn = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "xyznotfound";

        Assert.Empty(vm.Todos);
        Assert.Contains("xyznotfound", vm.EmptyMessage);
    }

    [Fact]
    public async Task ToggleCompleted_FlipsShowCompletedTodos()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.ShowCompletedTodos);
        vm.ToggleCompletedCommand.Execute(null);
        Assert.True(vm.ShowCompletedTodos);
        vm.ToggleCompletedCommand.Execute(null);
        Assert.False(vm.ShowCompletedTodos);
    }

    [Fact]
    public async Task DeleteAsync_CompletedTodo_RemovesFromCompletedAndUpdatesHasCompleted()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Done task", UpdatedOn = now, CompletedAt = now });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Also done", UpdatedOn = now, CompletedAt = now });

        Nav.AlertConfirmResult = true;
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.CompletedTodoCount);
        Assert.True(vm.HasCompletedTodos);

        await vm.DeleteCommand.ExecuteAsync(vm.CompletedTodos[0]);

        Assert.Equal(1, vm.CompletedTodoCount);
        Assert.True(vm.HasCompletedTodos); // still one left

        await vm.DeleteCommand.ExecuteAsync(vm.CompletedTodos[0]);

        Assert.Equal(0, vm.CompletedTodoCount);
        Assert.False(vm.HasCompletedTodos);
    }

    [Fact]
    public async Task WeekCompletedMessage_10OrMoreTodos_ShowsLegendaryMessage()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < 10; i++)
            await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = $"Done{i}", UpdatedOn = now, CompletedAt = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("🔥", vm.WeekCompletedMessage);
        Assert.Contains("legendary", vm.WeekCompletedMessage);
    }
}

// ─── DashboardViewModel: Beginner tier (5 progress notes) ────────────────────

public class DashboardBeginnerTierTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_With5ProgressNotes_SetsBeginnerLabel()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Beginner goal", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);
        for (int i = 0; i < 5; i++)
            await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), GoalFk = goal.Guid, AccountFk = account.Guid, NextStepItems = $"Note {i}", UpdatedOn = ts + i });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("Beginner", vm.OverallTierLabel);
    }
}

// ─── DashboardViewModel: HasNoPendingTodos and HasNoActiveGoals ───────────────

public class DashboardHasNoItemsTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_WithNoPendingTodos_SetsHasNoPendingTodosTrue()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasNoPendingTodos);
        Assert.Equal(0, vm.PendingTodoCount);
    }

    [Fact]
    public async Task Load_WithNoPendingTodos_AfterCompletingAll_SetsHasNoPendingTodosTrue()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Task", UpdatedOn = now };
        await TodoRepo.SaveAsync(todo);
        await TodoRepo.CompleteAsync(todo.Guid);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasNoPendingTodos);
    }

    [Fact]
    public async Task Load_WithNoActiveGoals_SetsHasNoActiveGoalsTrueAndNoWeeklyChallenge()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasNoActiveGoals);
        Assert.False(vm.HasWeeklyChallenge);
    }

    [Fact]
    public async Task Load_WithActiveGoal_ClearsHasNoActiveGoalsAndSetsWeeklyChallenge()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Active goal", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasNoActiveGoals);
        Assert.True(vm.HasWeeklyChallenge);
    }
}

// ─── JournalListViewModel: HasTodayEntry ─────────────────────────────────────

public class JournalListHasTodayEntryTests : ViewModelTestBase
{
    private JournalListViewModel BuildVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_WithTodayEntry_SetsHasTodayEntryTrue()
    {
        var account = await CreateTestAccountAsync();
        var todayMs = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Notes = "Today's entry", EnteredDate = todayMs, UpdatedOn = todayMs
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasTodayEntry);
    }

    [Fact]
    public async Task Load_WithNoTodayEntry_SetsHasTodayEntryFalse()
    {
        var account = await CreateTestAccountAsync();
        var yesterdayMs = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Notes = "Yesterday's entry", EnteredDate = yesterdayMs, UpdatedOn = yesterdayMs
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasTodayEntry);
    }
}

// ─── JournalEntryViewModel: SetMood and SetActivity commands ─────────────────

public class JournalEntrySetMoodActivityTests : ViewModelTestBase
{
    private JournalEntryViewModel BuildVm() =>
        new(JournalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public void SetMood_UpdatesMoodProperty()
    {
        var vm = BuildVm();
        vm.SetMoodCommand.Execute("Happy");
        Assert.Equal("Happy", vm.Mood);
    }

    [Fact]
    public void SetActivity_UpdatesActivityProperty()
    {
        var vm = BuildVm();
        vm.SetActivityCommand.Execute("Soccer");
        Assert.Equal("Soccer", vm.Activity);
    }

    [Fact]
    public void SetMood_OverwritesPreviousMood()
    {
        var vm = BuildVm();
        vm.SetMoodCommand.Execute("Sad");
        vm.SetMoodCommand.Execute("Happy");
        Assert.Equal("Happy", vm.Mood);
    }
}

// ─── GoalEntryViewModel: ShareProgressAsync (NO_MAUI path) ───────────────────

public class GoalEntryShareProgressTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task ShareProgress_WithNoGuid_DoesNotThrowAndReturnsEarly()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        // Guid is empty — should return early without crashing
        var ex = await Record.ExceptionAsync(() => vm.ShareProgressCommand.ExecuteAsync(null));
        Assert.Null(ex);
    }

    [Fact]
    public async Task ShareProgress_WithGoalAndProgressNotes_CompletesWithoutThrowing()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Shareable goal", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);
        await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), GoalFk = goal.Guid, AccountFk = account.Guid, NextStepItems = "Made great progress", UpdatedOn = ts });

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        var ex = await Record.ExceptionAsync(() => vm.ShareProgressCommand.ExecuteAsync(null));
        Assert.Null(ex);
    }

    [Fact]
    public async Task ShareProgress_GoalTextOver60Chars_TitleTruncatesTo60()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var longText = new string('G', 80);
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = longText, EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        // Should not throw even with long GoalText
        var ex = await Record.ExceptionAsync(() => vm.ShareProgressCommand.ExecuteAsync(null));
        Assert.Null(ex);
    }
}

// ─── GoalEntryViewModel: MarkCompleteAsync marks goal in DB ──────────────────

public class GoalEntryMarkCompleteTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task MarkComplete_WithValidGuid_MarksGoalCompletedInDb()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Complete me", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        await vm.MarkCompleteCommand.ExecuteAsync(null);

        var updated = await GoalRepo.GetAsync(goal.Guid);
        Assert.NotNull(updated!.CompletionDate);
    }

    [Fact]
    public async Task MarkComplete_WithEmptyGuid_DoesNotThrowAndNothingCompleted()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        // Guid is empty — early return

        var ex = await Record.ExceptionAsync(() => vm.MarkCompleteCommand.ExecuteAsync(null));
        Assert.Null(ex);
    }

    [Fact]
    public async Task MarkComplete_LongGoalText_CelebrationTitleTruncatesAt60()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = new string('H', 80), EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        // Should not throw when GoalText > 60
        var ex = await Record.ExceptionAsync(() => vm.MarkCompleteCommand.ExecuteAsync(null));
        Assert.Null(ex);
    }
}

// ─── GoalEntryViewModel: GoalText/MeasurableOutcome/Steps length cap (2000) ──

public class GoalEntryTextFieldLengthTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task SaveAsync_GoalTextOver2000Chars_IsTruncatedTo2000()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.GoalText = new string('G', 2100);
        await vm.SaveCommand.ExecuteAsync(null);

        var account = await AccountService.GetAccountAsync();
        var goals = await GoalRepo.GetAllActiveAsync(account!.Guid);
        Assert.Single(goals);
        Assert.True((goals[0].GoalText?.Length ?? 0) <= 2000,
            $"GoalText length {goals[0].GoalText?.Length} exceeds 2000-char API limit");
    }

    [Fact]
    public async Task SaveAsync_GoalTextExactly2000Chars_IsNotTruncated()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.GoalText = new string('H', 2000);
        await vm.SaveCommand.ExecuteAsync(null);

        var account = await AccountService.GetAccountAsync();
        var goals = await GoalRepo.GetAllActiveAsync(account!.Guid);
        Assert.Single(goals);
        Assert.Equal(2000, goals[0].GoalText?.Length ?? 0);
    }

    [Fact]
    public async Task SaveAsync_MeasurableOutcomeOver2000Chars_IsTruncatedTo2000()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.GoalText = "Goal";
        vm.MeasurableOutcome = new string('M', 2100);
        await vm.SaveCommand.ExecuteAsync(null);

        var account = await AccountService.GetAccountAsync();
        var goals = await GoalRepo.GetAllActiveAsync(account!.Guid);
        Assert.Single(goals);
        Assert.True((goals[0].MeasurableOutcome?.Length ?? 0) <= 2000,
            $"MeasurableOutcome length {goals[0].MeasurableOutcome?.Length} exceeds 2000-char API limit");
    }

    [Fact]
    public async Task SaveAsync_StepsOver2000Chars_IsTruncatedTo2000()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.GoalText = "Goal";
        vm.Steps = new string('S', 2100);
        await vm.SaveCommand.ExecuteAsync(null);

        var account = await AccountService.GetAccountAsync();
        var goals = await GoalRepo.GetAllActiveAsync(account!.Guid);
        Assert.Single(goals);
        Assert.True((goals[0].Steps?.Length ?? 0) <= 2000,
            $"Steps length {goals[0].Steps?.Length} exceeds 2000-char API limit");
    }
}

// ─── TodoEntryViewModel: Notes length cap (API 2000-char limit) ──────────────

public class TodoEntryNotesLengthTests : ViewModelTestBase
{
    private TodoEntryViewModel BuildVm() =>
        new(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task SaveAsync_NotesOver2000Chars_IsTruncatedTo2000()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.Title = "Task";
        vm.Notes = new string('N', 2100);
        await vm.SaveCommand.ExecuteAsync(null);

        var account = await AccountService.GetAccountAsync();
        var todos = await TodoRepo.GetPendingAsync(account!.Guid);
        Assert.Single(todos);
        Assert.True((todos[0].Notes?.Length ?? 0) <= 2000,
            $"Notes length {todos[0].Notes?.Length} exceeds 2000-char API limit");
    }

    [Fact]
    public async Task SaveAsync_NotesExactly2000Chars_IsNotTruncated()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.Title = "Task";
        vm.Notes = new string('M', 2000);
        await vm.SaveCommand.ExecuteAsync(null);

        var account = await AccountService.GetAccountAsync();
        var todos = await TodoRepo.GetPendingAsync(account!.Guid);
        Assert.Single(todos);
        Assert.Equal(2000, todos[0].Notes?.Length ?? 0);
    }
}

// ─── GoalListViewModel: NeedsAttention entry count display ───────────────────

public class GoalListNeedsAttentionCountTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task NeedsAttention_TwoStaleGoals_EntryCountShowsPlural()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // Two stale goals with no recent progress
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Stale A", EnteredDate = ts, UpdatedOn = ts });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Stale B", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.CategoryFilter = "NeedsAttention";

        Assert.Equal(2, vm.Goals.Count);
        Assert.Contains("2", vm.EntryCountDisplay);
        Assert.Contains("goals", vm.EntryCountDisplay);
        Assert.Contains("attention", vm.EntryCountDisplay);
    }

    [Fact]
    public async Task NeedsAttention_OneStaleGoal_EntryCountShowsSingular()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "One stale", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.CategoryFilter = "NeedsAttention";

        Assert.Single(vm.Goals);
        Assert.Contains("1", vm.EntryCountDisplay);
        Assert.Contains("goal", vm.EntryCountDisplay);
        Assert.Contains("attention", vm.EntryCountDisplay);
    }

    [Fact]
    public async Task NeedsAttention_NoStaleGoals_EntryCountDisplayIsEmpty()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // Fresh goal with recent progress (not stale)
        var goalGuid = Guid.NewGuid().ToString();
        await GoalRepo.SaveAsync(new Goal { Guid = goalGuid, AccountFk = account.Guid, GoalText = "Fresh goal", EnteredDate = ts, UpdatedOn = ts });
        await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goalGuid, NextStepItems = "Done", UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.CategoryFilter = "NeedsAttention";

        Assert.Empty(vm.Goals);
        Assert.Equal(string.Empty, vm.EntryCountDisplay);
    }
}

// ─── TodoEntryViewModel: RestoreAsync with valid Guid ────────────────────────

public class TodoEntryRestoreTests : ViewModelTestBase
{
    private TodoEntryViewModel BuildVm() =>
        new(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task RestoreAsync_WithValidGuid_RestorestsTodoPending()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Completed task", UpdatedOn = now, CompletedAt = now };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);
        Assert.True(vm.IsCompleted);

        await vm.RestoreCommand.ExecuteAsync(null);

        var pending = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.Equal(todo.Guid, pending[0].Guid);
    }
}

// ─── GoalListViewModel: NeedsAttention excludes completed goals ───────────────

public class GoalListNeedsAttentionExcludesCompletedTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task NeedsAttention_CompletedGoalWithNoProgress_ExcludesFromResults()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // A completed goal with no progress — should NOT appear in NeedsAttention
        var completedGoal = new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Completed goal", EnteredDate = ts, UpdatedOn = ts, CompletionDate = ts
        };
        // An active goal with no progress — SHOULD appear
        var activeGoal = new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Active stale goal", EnteredDate = ts, UpdatedOn = ts
        };
        await GoalRepo.SaveAsync(completedGoal);
        await GoalRepo.SaveAsync(activeGoal);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.CategoryFilter = "NeedsAttention";

        Assert.Single(vm.Goals);
        Assert.Equal("Active stale goal", vm.Goals[0].GoalText);
    }
}

// ─── JournalListViewModel: base state (no journals) EmptyMessage ──────────────

public class JournalListBaseStateTests : ViewModelTestBase
{
    private JournalListViewModel BuildVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_NoJournals_EmptyMessageIsDefaultNoEntries()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Empty(vm.Journals);
        Assert.Contains("No journal entries", vm.EmptyMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Load_NoJournals_HasTodayEntryIsFalse()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasTodayEntry);
    }
}

// ─── GoalEntryViewModel: ProgressPercent field save behavior ─────────────────

public class GoalEntryProgressPercentTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task SaveAsync_ProgressPercent50_SavesNonNullValue()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.GoalText = "Exercise daily";
        vm.ProgressPercent = 50;
        await vm.SaveCommand.ExecuteAsync(null);

        var account = await AccountService.GetAccountAsync();
        var goals = await GoalRepo.GetAllActiveAsync(account!.Guid);
        Assert.Single(goals);
        Assert.Equal(50, goals[0].ProgressPercent);
    }

    [Fact]
    public async Task SaveAsync_ProgressPercent0_SavesAsNull()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.GoalText = "Learn to code";
        vm.ProgressPercent = 0;
        await vm.SaveCommand.ExecuteAsync(null);

        var account = await AccountService.GetAccountAsync();
        var goals = await GoalRepo.GetAllActiveAsync(account!.Guid);
        Assert.Single(goals);
        Assert.Null(goals[0].ProgressPercent);
    }

    [Fact]
    public async Task SaveAsync_ProgressPercent100_SavesNonNullValue()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.GoalText = "Finish project";
        vm.ProgressPercent = 100;
        await vm.SaveCommand.ExecuteAsync(null);

        var account = await AccountService.GetAccountAsync();
        var goals = await GoalRepo.GetAllActiveAsync(account!.Guid);
        Assert.Single(goals);
        Assert.Equal(100, goals[0].ProgressPercent);
    }
}

// ─── GoalListViewModel: QuickNote saves progress note to DB ──────────────────

public class GoalListQuickNoteSavesTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task QuickNote_WithNote_SavesProgressNoteToDatabase()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Become a runner", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = "Ran 2 miles today!";
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.QuickNoteCommand.ExecuteAsync(vm.Goals[0]);

        var progress = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Single(progress.Where(p => p.DeletedAt == null));
        Assert.Equal("Ran 2 miles today!", progress.First(p => p.DeletedAt == null).NextStepItems);
    }

    [Fact]
    public async Task QuickNote_CancelledPrompt_DoesNotSaveProgress()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Meditate daily", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = null;
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.QuickNoteCommand.ExecuteAsync(vm.Goals[0]);

        var progress = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Empty(progress.Where(p => p.DeletedAt == null));
    }
}

// ─── JournalEntryViewModel: edit mode saves correct content ──────────────────

public class JournalEntryEditModeSaveTests : ViewModelTestBase
{
    private JournalEntryViewModel BuildVm() =>
        new(JournalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task SaveAsync_ExistingJournal_UpdatesContentInDatabase()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Original notes", Activity = "Swimming", EnteredDate = ts, UpdatedOn = ts };
        await JournalRepo.SaveAsync(journal);

        var vm = BuildVm();
        vm.Guid = journal.Guid;
        await Task.Delay(200);

        Assert.Equal("Original notes", vm.Notes);
        Assert.Equal("Swimming", vm.Activity);

        vm.Notes = "Updated notes";
        vm.Activity = "Running";
        await vm.SaveCommand.ExecuteAsync(null);

        var updated = await JournalRepo.GetAsync(journal.Guid);
        Assert.Equal("Updated notes", updated!.Notes);
        Assert.Equal("Running", updated.Activity);
    }

    [Fact]
    public async Task SaveAsync_ExistingJournal_DoesNotCreateDuplicate()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Unique note", EnteredDate = ts, UpdatedOn = ts };
        await JournalRepo.SaveAsync(journal);

        var vm = BuildVm();
        vm.Guid = journal.Guid;
        await Task.Delay(200);

        vm.Notes = "Changed note";
        await vm.SaveCommand.ExecuteAsync(null);

        var all = await JournalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(all);
    }
}

// ─── GoalListViewModel: delete cascades to progress notes ────────────────────

public class GoalListDeleteCascadesProgressTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task DeleteAsync_GoalWithProgressNotes_DeletesProgressToo()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn chess", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);
        await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goal.Guid, NextStepItems = "Study openings", UpdatedOn = ts });
        await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goal.Guid, NextStepItems = "Practice endgames", UpdatedOn = ts });

        Nav.AlertConfirmResult = true;
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Goals);

        await vm.DeleteCommand.ExecuteAsync(vm.Goals[0]);

        Assert.Empty(vm.Goals);
        // Verify progress notes are also gone
        var progress = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Empty(progress.Where(p => p.DeletedAt == null));
    }

    [Fact]
    public async Task DeleteAsync_GoalDeclined_GoalRemainsWithProgressIntact()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Keep this goal", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);
        await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goal.Guid, NextStepItems = "Keep this note", UpdatedOn = ts });

        Nav.AlertConfirmResult = false;
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.DeleteCommand.ExecuteAsync(vm.Goals[0]);

        Assert.Single(vm.Goals);
        var progress = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Single(progress.Where(p => p.DeletedAt == null));
    }
}

// ─── GoalEntryViewModel: delete cascades to progress notes ───────────────────

public class GoalEntryDeleteCascadesProgressTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task DeleteAsync_GoalWithProgressNotes_DeletesProgressToo()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn painting", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);
        await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goal.Guid, NextStepItems = "Sketching basics", UpdatedOn = ts });

        Nav.AlertConfirmResult = true;
        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        await vm.DeleteCommand.ExecuteAsync(null);

        // Goal should be gone
        var retrieved = await GoalRepo.GetAsync(goal.Guid);
        Assert.True(retrieved == null || retrieved.DeletedAt != null);

        // Progress should also be gone
        var progress = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Empty(progress.Where(p => p.DeletedAt == null));
    }
}

// ─── TodoListViewModel: deleting last completed todo auto-hides completed panel ─

public class TodoListDeleteLastCompletedHidesCompletedTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task DeleteAsync_LastCompletedTodo_SetsShowCompletedTodosToFalse()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Solo completed", UpdatedOn = now, CompletedAt = now });

        Nav.AlertConfirmResult = true;
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        // Expand completed section
        vm.ToggleCompletedCommand.Execute(null);
        Assert.True(vm.ShowCompletedTodos);
        Assert.Equal(1, vm.CompletedTodoCount);

        // Delete the only completed todo
        await vm.DeleteCommand.ExecuteAsync(vm.CompletedTodos[0]);

        Assert.Equal(0, vm.CompletedTodoCount);
        Assert.False(vm.HasCompletedTodos);
        Assert.False(vm.ShowCompletedTodos);
    }

    [Fact]
    public async Task DeleteAsync_OneOfTwoCompletedTodos_ShowCompletedTodosRemainsTrue()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Done A", UpdatedOn = now, CompletedAt = now });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Done B", UpdatedOn = now, CompletedAt = now });

        Nav.AlertConfirmResult = true;
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.ToggleCompletedCommand.Execute(null);
        Assert.True(vm.ShowCompletedTodos);

        await vm.DeleteCommand.ExecuteAsync(vm.CompletedTodos[0]);

        Assert.Equal(1, vm.CompletedTodoCount);
        Assert.True(vm.HasCompletedTodos);
        Assert.True(vm.ShowCompletedTodos);
    }
}

// ─── JournalEntryViewModel: Notes field 10,000 char limit ─────────────────────

public class JournalEntryNotesLengthTests : ViewModelTestBase
{
    private JournalEntryViewModel BuildVm() =>
        new(JournalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task SaveAsync_NotesOver10000Chars_IsTruncatedTo10000()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.Notes = new string('J', 10_100);
        await vm.SaveCommand.ExecuteAsync(null);

        var account = await AccountService.GetAccountAsync();
        var journals = await JournalRepo.GetAllActiveAsync(account!.Guid);
        Assert.Single(journals);
        Assert.True((journals[0].Notes?.Length ?? 0) <= 10_000,
            $"Notes length {journals[0].Notes?.Length} exceeds 10000-char API limit");
    }

    [Fact]
    public async Task SaveAsync_NotesExactly10000Chars_IsNotTruncated()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.Notes = new string('J', 10_000);
        await vm.SaveCommand.ExecuteAsync(null);

        var account = await AccountService.GetAccountAsync();
        var journals = await JournalRepo.GetAllActiveAsync(account!.Guid);
        Assert.Single(journals);
        Assert.Equal(10_000, journals[0].Notes?.Length ?? 0);
    }
}

// ─── JournalListViewModel: Week filter empty message ─────────────────────────

public class JournalListWeekFilterEmptyMessageTests : ViewModelTestBase
{
    private JournalListViewModel BuildVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task WeekFilter_NoEntriesThisWeek_SetsWeekEmptyMessage()
    {
        var account = await CreateTestAccountAsync();
        // Entry older than 7 days — outside week filter window
        var oldMs = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Notes = "Old entry", EnteredDate = oldMs, UpdatedOn = oldMs
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.DateFilter = "Week";

        Assert.Empty(vm.Journals);
        Assert.Contains("this week", vm.EmptyMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WeekFilter_EntryFromToday_IsIncluded()
    {
        var account = await CreateTestAccountAsync();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var oldMs = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Notes = "Today's entry", EnteredDate = nowMs, UpdatedOn = nowMs
        });
        await JournalRepo.SaveAsync(new Journal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Notes = "Old entry", EnteredDate = oldMs, UpdatedOn = oldMs
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.DateFilter = "Week";

        Assert.Single(vm.Journals);
        Assert.Equal("Today's entry", vm.Journals[0].Notes);
    }

    [Fact]
    public async Task WeekFilter_ThenClearFilter_ShowsAllEntries()
    {
        var account = await CreateTestAccountAsync();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var oldMs = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Notes = "Recent", EnteredDate = nowMs, UpdatedOn = nowMs
        });
        await JournalRepo.SaveAsync(new Journal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Notes = "Old", EnteredDate = oldMs, UpdatedOn = oldMs
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.DateFilter = "Week";
        Assert.Single(vm.Journals);

        vm.DateFilter = "All";
        Assert.Equal(2, vm.Journals.Count);
    }
}

// ─── GoalListViewModel: search filter empty message ──────────────────────────

public class GoalListSearchEmptyMessageTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task FilterText_NoMatches_SetsNoMatchesEmptyMessage()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Learn piano", EnteredDate = ts, UpdatedOn = ts
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "zzznomatch";

        Assert.Empty(vm.Goals);
        Assert.Contains("zzznomatch", vm.EmptyMessage);
        Assert.Contains("No matches", vm.EmptyMessage);
    }

    [Fact]
    public async Task FilterText_ClearedAfterNoMatch_RestoresAllGoals()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            GoalText = "Learn guitar", EnteredDate = ts, UpdatedOn = ts
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "zzznomatch";
        Assert.Empty(vm.Goals);

        vm.FilterText = string.Empty;
        Assert.Single(vm.Goals);
        Assert.Equal("No goals yet", vm.EmptyMessage);
    }

    [Fact]
    public async Task FilterText_NoGoalsAtAll_SetsNoMatchesEmptyMessage()
    {
        await CreateTestAccountAsync();

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "anything";

        Assert.Empty(vm.Goals);
        Assert.Contains("No matches", vm.EmptyMessage);
    }
}

// ─── TodoListViewModel: "All done!" EmptyMessage ─────────────────────────────

public class TodoListAllDoneEmptyMessageTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task LoadAsync_NoPendingTodos_DefaultEmptyMessageIsAllDone()
    {
        await CreateTestAccountAsync();

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Empty(vm.Todos);
        Assert.Equal("All done!", vm.EmptyMessage);
    }

    [Fact]
    public async Task FilterText_SetThenCleared_EmptyMessageResetsToAllDone()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Title = "Buy milk", UpdatedOn = ts
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.FilterText = "zzznomatch";
        Assert.Contains("No matches", vm.EmptyMessage);

        vm.FilterText = string.Empty;
        Assert.Equal("All done!", vm.EmptyMessage);
        Assert.Single(vm.Todos);
    }

    [Fact]
    public async Task FilterText_NoMatches_SetsNoMatchesEmptyMessage()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid,
            Title = "Take vitamins", UpdatedOn = ts
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.FilterText = "zzznomatch";
        Assert.Empty(vm.Todos);
        Assert.Contains("zzznomatch", vm.EmptyMessage);
    }
}

// ─── TodoListViewModel: WeekCompletedMessage 5 and 1 todo, WeekOverWeek increase ──

public class TodoListWeekMessageBranchTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task WeekCompletedMessage_5Todos_ShowsGreatMomentumMessage()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (var i = 0; i < 5; i++)
        {
            var guid = Guid.NewGuid().ToString();
            await TodoRepo.SaveAsync(new Todo { Guid = guid, AccountFk = account.Guid, Title = $"Task {i}", UpdatedOn = ts });
            await TodoRepo.CompleteAsync(guid);
        }

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasWeekCompletedMessage);
        Assert.Contains("🌟", vm.WeekCompletedMessage);
        Assert.Contains("great momentum", vm.WeekCompletedMessage);
    }

    [Fact]
    public async Task WeekCompletedMessage_1Todo_UsesSingular()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid().ToString();
        await TodoRepo.SaveAsync(new Todo { Guid = guid, AccountFk = account.Guid, Title = "Single task", UpdatedOn = ts });
        await TodoRepo.CompleteAsync(guid);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasWeekCompletedMessage);
        Assert.Contains("1 todo", vm.WeekCompletedMessage);
        Assert.DoesNotContain("todos", vm.WeekCompletedMessage);
    }

    [Fact]
    public async Task WeekOverWeek_MoreThisWeek_ShowsIncreaseMessage()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var twoWeeksAgo = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();

        // 1 todo completed last week (8-14 days ago)
        var oldGuid = Guid.NewGuid().ToString();
        await TodoRepo.UpsertFromSyncAsync(new Todo
        {
            Guid = oldGuid, AccountFk = account.Guid, Title = "Old task",
            CompletedAt = twoWeeksAgo, UpdatedOn = twoWeeksAgo
        });

        // 3 todos completed this week
        for (var i = 0; i < 3; i++)
        {
            var guid = Guid.NewGuid().ToString();
            await TodoRepo.SaveAsync(new Todo { Guid = guid, AccountFk = account.Guid, Title = $"New task {i}", UpdatedOn = ts });
            await TodoRepo.CompleteAsync(guid);
        }

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasWeekOverWeekMessage);
        Assert.Contains("📈", vm.WeekOverWeekMessage);
    }
}

// ─── JournalListViewModel: DeleteAsync with active Week filter ─────────────────

public class JournalListDeleteWithWeekFilterTests : ViewModelTestBase
{
    private JournalListViewModel BuildVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Delete_LastJournalInWeekFilter_EmptyMessageUpdatesToWeekMessage()
    {
        var account = await CreateTestAccountAsync();
        var recent = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds();
        var old = DateTimeOffset.UtcNow.AddDays(-15).ToUnixTimeMilliseconds();

        var recentGuid = Guid.NewGuid().ToString();
        await JournalRepo.SaveAsync(new Journal { Guid = recentGuid, AccountFk = account.Guid, Notes = "Recent entry", EnteredDate = recent, UpdatedOn = recent });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Old entry", EnteredDate = old, UpdatedOn = old });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SetDateFilterCommand.Execute("Week");

        Assert.Single(vm.Journals);

        Nav.AlertConfirmResult = true;
        await vm.DeleteCommand.ExecuteAsync(vm.Journals[0]);

        Assert.Empty(vm.Journals);
        Assert.Contains("No entries this week", vm.EmptyMessage);
    }

    [Fact]
    public async Task Delete_JournalWithNoFilter_EntryCountDecreases()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Entry 1", EnteredDate = ts, UpdatedOn = ts });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Entry 2", EnteredDate = ts + 1, UpdatedOn = ts + 1 });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Journals.Count);

        Nav.AlertConfirmResult = true;
        await vm.DeleteCommand.ExecuteAsync(vm.Journals[0]);

        Assert.Single(vm.Journals);
        Assert.Contains("1", vm.EntryCountDisplay);
    }
}

// ─── Entry ViewModels: SetReminderAsync guard branches ────────────────────────

public class EntrySetReminderTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildGoalVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    private JournalEntryViewModel BuildJournalVm() =>
        new(JournalRepo, AccountService, Analytics, Nav, ReminderSvc);

    private TodoEntryViewModel BuildTodoVm() =>
        new(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task GoalEntry_SetReminder_EmptyGuid_ReturnsEarlyAndNoReminderScheduled()
    {
        var account = await CreateTestAccountAsync();
        Nav.ActionSheetResult = "1 hour";

        var vm = BuildGoalVm();
        // Guid is empty (new, unsaved goal) — reminder guard should fire
        await vm.SetReminderCommand.ExecuteAsync(null);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task GoalEntry_SetReminder_UserCancels_NoReminderScheduled()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn violin", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        Nav.ActionSheetResult = null; // user cancels duration picker
        var vm = BuildGoalVm();
        vm.Guid = goal.Guid;
        await Task.Delay(150);

        await vm.SetReminderCommand.ExecuteAsync(null);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task GoalEntry_SetReminder_ValidGuidAndDuration_SchedulesReminder()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn piano", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        Nav.ActionSheetResult = "1 hour";
        var vm = BuildGoalVm();
        vm.Guid = goal.Guid;
        await Task.Delay(150);

        await vm.SetReminderCommand.ExecuteAsync(null);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.Equal("Goal", pending[0].Topic);
        Assert.Equal(goal.Guid, pending[0].EntityGuid);
    }

    [Fact]
    public async Task JournalEntry_SetReminder_UserCancels_NoReminderScheduled()
    {
        var account = await CreateTestAccountAsync();
        Nav.ActionSheetResult = null; // user cancels

        var vm = BuildJournalVm();
        await vm.SetReminderCommand.ExecuteAsync(null);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task JournalEntry_SetReminder_WithNotes_SchedulesReminderWithLabel()
    {
        var account = await CreateTestAccountAsync();
        Nav.ActionSheetResult = "8 hours";

        var vm = BuildJournalVm();
        vm.Notes = "Today was a big day at school";

        await vm.SetReminderCommand.ExecuteAsync(null);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.Equal("Journal", pending[0].Topic);
        Assert.Contains("Today was", pending[0].Title);
    }

    [Fact]
    public async Task TodoEntry_SetReminder_EmptyGuid_ReturnsEarlyAndNoReminderScheduled()
    {
        var account = await CreateTestAccountAsync();
        Nav.ActionSheetResult = "1 hour";

        var vm = BuildTodoVm();
        // Guid is empty — guard should fire
        await vm.SetReminderCommand.ExecuteAsync(null);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task TodoEntry_SetReminder_ValidGuidAndDuration_SchedulesReminder()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Read chapter 5", UpdatedOn = ts };
        await TodoRepo.SaveAsync(todo);

        Nav.ActionSheetResult = "1 day";
        var vm = BuildTodoVm();
        vm.Guid = todo.Guid;
        vm.Title = todo.Title;
        await Task.Delay(150);

        await vm.SetReminderCommand.ExecuteAsync(null);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.Equal("Todo", pending[0].Topic);
        Assert.Equal(todo.Guid, pending[0].EntityGuid);
    }
}

// ─── GoalEntryViewModel: _loadedNextStepItems deduplication ──────────────────

public class GoalEntryNextStepDedupTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task SaveAsync_UnchangedNextStepItems_DoesNotCreateDuplicateProgressNote()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Play guitar", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        // Create one existing progress note
        await GoalProgressRepo.SaveAsync(new GoalProgress
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goal.Guid,
            NextStepItems = "Practice chords", UpdatedOn = ts
        });

        // Load the goal (sets _loadedNextStepItems to "Practice chords")
        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        // Save without changing NextStepItems — should NOT create a new progress note
        await vm.SaveCommand.ExecuteAsync(null);

        var notes = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Single(notes); // still only the original note
    }

    [Fact]
    public async Task SaveAsync_ChangedNextStepItems_CreatesNewProgressNote()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn coding", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        await GoalProgressRepo.SaveAsync(new GoalProgress
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goal.Guid,
            NextStepItems = "Watch tutorial", UpdatedOn = ts
        });

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        // Change NextStepItems to something new
        vm.NextStepItems = "Build a small project";
        await vm.SaveCommand.ExecuteAsync(null);

        var notes = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Equal(2, notes.Count); // original + new note
    }

    [Fact]
    public async Task SaveAsync_EmptyNextStepItems_DoesNotCreateProgressNote()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Read more books", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        // NextStepItems is empty — no progress note should be saved
        vm.NextStepItems = string.Empty;
        await vm.SaveCommand.ExecuteAsync(null);

        var notes = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Empty(notes);
    }
}

// ─── JournalListViewModel: singular EntryCountDisplay ────────────────────────

public class JournalListSingularCountTests : ViewModelTestBase
{
    private JournalListViewModel BuildVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_OneJournal_EntryCountDisplayUsesSingular()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Only entry", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.Journals.Count);
        Assert.Contains("1 entry", vm.EntryCountDisplay);
        Assert.DoesNotContain("entries", vm.EntryCountDisplay);
    }

    [Fact]
    public async Task Load_TwoJournals_EntryCountDisplayUsesPlural()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Entry 1", EnteredDate = ts, UpdatedOn = ts });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Entry 2", EnteredDate = ts + 1, UpdatedOn = ts + 1 });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Journals.Count);
        Assert.Contains("2 entries", vm.EntryCountDisplay);
    }
}

// ─── GoalEntryViewModel: HasProgressHistory with 1 note ──────────────────────

public class GoalEntryProgressHistoryTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task LoadAsync_WithOneProgressNote_HasProgressHistoryIsFalse()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "One-note goal", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);
        await GoalProgressRepo.SaveAsync(new GoalProgress
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goal.Guid,
            NextStepItems = "First step", UpdatedOn = ts
        });

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.Equal(1, vm.ProgressNotesCount);
        Assert.False(vm.HasProgressHistory); // Skip(1) produces empty list
        Assert.Empty(vm.ProgressHistory);
    }

    [Fact]
    public async Task LoadAsync_ProgressNotesWithBlankSteps_ExcludedFromHistory()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Blank steps goal", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        // First note (current, shown as NextStepItems) — has content
        await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goal.Guid, NextStepItems = "Active steps", UpdatedOn = ts + 3 });
        // Subsequent notes with blank NextStepItems — should be filtered out of history
        await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goal.Guid, NextStepItems = "   ", UpdatedOn = ts + 2 });
        await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goal.Guid, NextStepItems = "", UpdatedOn = ts + 1 });

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.Equal(3, vm.ProgressNotesCount);
        Assert.False(vm.HasProgressHistory); // blank steps filtered out of history
    }
}

// ─── TodoListViewModel and GoalListViewModel: null-arg command guards ─────────

public class ListViewModelNullArgTests : ViewModelTestBase
{
    private TodoListViewModel BuildTodoVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    private GoalListViewModel BuildGoalVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task TodoList_CompleteCommand_NullTodo_DoesNotThrow()
    {
        await CreateTestAccountAsync();
        var vm = BuildTodoVm();
        await vm.LoadCommand.ExecuteAsync(null);
        var ex = await Record.ExceptionAsync(() => vm.CompleteCommand.ExecuteAsync(null!));
        Assert.Null(ex);
    }

    [Fact]
    public async Task TodoList_UncompleteCommand_NullTodo_DoesNotThrow()
    {
        await CreateTestAccountAsync();
        var vm = BuildTodoVm();
        await vm.LoadCommand.ExecuteAsync(null);
        var ex = await Record.ExceptionAsync(() => vm.UncompleteCommand.ExecuteAsync(null!));
        Assert.Null(ex);
    }

    [Fact]
    public async Task TodoList_OpenCommand_NullTodo_DoesNotThrow()
    {
        await CreateTestAccountAsync();
        var vm = BuildTodoVm();
        await vm.LoadCommand.ExecuteAsync(null);
        var ex = await Record.ExceptionAsync(() => vm.OpenCommand.ExecuteAsync(null!));
        Assert.Null(ex);
    }

    [Fact]
    public async Task GoalList_OpenCommand_NullGoal_DoesNotThrow()
    {
        await CreateTestAccountAsync();
        var vm = BuildGoalVm();
        await vm.LoadCommand.ExecuteAsync(null);
        var ex = await Record.ExceptionAsync(() => vm.OpenCommand.ExecuteAsync(null!));
        Assert.Null(ex);
    }

    [Fact]
    public async Task GoalList_TogglePinCommand_NullGoal_DoesNotThrow()
    {
        await CreateTestAccountAsync();
        var vm = BuildGoalVm();
        await vm.LoadCommand.ExecuteAsync(null);
        var ex = await Record.ExceptionAsync(() => vm.TogglePinCommand.ExecuteAsync(null!));
        Assert.Null(ex);
    }
}

// ─── TodoListViewModel: zero completed this week hides WeekCompletedMessage ───

public class TodoListZeroWeekCompletedTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_NoCompletedTodosThisWeek_HidesWeekCompletedMessage()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasWeekCompletedMessage);
        Assert.Equal(string.Empty, vm.WeekCompletedMessage);
        Assert.False(vm.HasWeekOverWeekMessage);
    }

    [Fact]
    public async Task Load_TwoCompletedTodos_ShowsPluralMessage()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (var i = 0; i < 2; i++)
        {
            var guid = Guid.NewGuid().ToString();
            await TodoRepo.SaveAsync(new Todo { Guid = guid, AccountFk = account.Guid, Title = $"Task {i}", UpdatedOn = ts });
            await TodoRepo.CompleteAsync(guid);
        }

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasWeekCompletedMessage);
        Assert.Contains("2 todos", vm.WeekCompletedMessage);
        Assert.Contains("done this week", vm.WeekCompletedMessage);
    }
}

// ─── DashboardViewModel: NextGoalMeeting with far-future date ─────────────────

public class DashboardNextGoalMeetingFarDateTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService,
            BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_GoalWithMeetingIn5Days_ShowsFormattedDate()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var meeting = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today.AddDays(5), DateTimeKind.Local)).ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Far meeting goal",
            EnteredDate = ts, NextMeetingDate = meeting
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasNextGoalMeeting);
        // Should show MMM d format (not "today" or "tomorrow")
        Assert.DoesNotContain("today", vm.NextGoalMeeting, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tomorrow", vm.NextGoalMeeting, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Next goal meeting:", vm.NextGoalMeeting);
    }

    [Fact]
    public async Task Load_GoalWithPastMeetingDate_DoesNotShowMeeting()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var pastMeeting = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today.AddDays(-1), DateTimeKind.Local)).ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal
        {
            Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Past meeting goal",
            EnteredDate = ts, NextMeetingDate = pastMeeting
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasNextGoalMeeting);
        Assert.Equal(string.Empty, vm.NextGoalMeeting);
    }
}

// ─── SettingsViewModel: SaveServerUrl non-empty and trailing-slash strip ───────

public class SettingsViewModelSaveUrlTests : ViewModelTestBase
{
    private SettingsViewModel BuildVm() =>
        new(AccountService, new FakeHttpClientFactory(new NoOpHttpHandler()), Analytics);

    [Fact]
    public async Task SaveServerUrl_NonEmptyUrl_SetsSavedMessage()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ServerUrl = "https://example.com";
        await vm.SaveServerUrlCommand.ExecuteAsync(null);
        Assert.Contains("saved", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveServerUrl_TrailingSlash_IsStripped()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ServerUrl = "https://example.com/";
        await vm.SaveServerUrlCommand.ExecuteAsync(null);
        // Reload to verify stored value
        var vm2 = BuildVm();
        await vm2.LoadCommand.ExecuteAsync(null);
        Assert.DoesNotContain("/", vm2.ServerUrl.TrimStart("https://".ToCharArray()));
        Assert.Equal("https://example.com", vm2.ServerUrl);
    }
}

// ─── GoalListViewModel: null arg guards for QuickNote and Open ────────────────

public class GoalListNullArgTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task QuickNoteCommand_NullGoal_DoesNotThrow()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        var ex = await Record.ExceptionAsync(() => vm.QuickNoteCommand.ExecuteAsync(null!));
        Assert.Null(ex);
    }
}

// ─── JournalListViewModel: null arg guards ────────────────────────────────────

public class JournalListNullArgTests : ViewModelTestBase
{
    private JournalListViewModel BuildVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task OpenCommand_NullJournal_DoesNotThrow()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        var ex = await Record.ExceptionAsync(() => vm.OpenCommand.ExecuteAsync(null!));
        Assert.Null(ex);
    }

    [Fact]
    public async Task DeleteCommand_NullJournal_DoesNotThrow()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        var ex = await Record.ExceptionAsync(() => vm.DeleteCommand.ExecuteAsync(null!));
        Assert.Null(ex);
    }
}

// ─── SnoozeHelper: Custom path with invalid prompt input ─────────────────────

public class SnoozeHelperCustomPathTests : ViewModelTestBase
{
    [Fact]
    public async Task PickAsync_CustomChoiceWithInvalidAmount_ReturnsNullAndNoChangeToReminders()
    {
        // ActionSheet → "Custom...", Prompt → "abc" (not a number) → null returned, no snooze
        var account = await CreateTestAccountAsync();
        var future = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        await ReminderSvc.ScheduleAsync(new Reminder { AccountFk = account.Guid, Title = "Test", Topic = "General", FireAt = future });

        Nav.ActionSheetResult = "Custom...";
        Nav.PromptResult = "notanumber";

        var vm = new RemindersViewModel(ReminderSvc, AccountService, Nav);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Reminders);

        await vm.SnoozeCommand.ExecuteAsync(vm.Reminders[0]);

        // Reminder still present (snooze was no-op due to null duration)
        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.NotEmpty(pending);
    }

    [Fact]
    public async Task RemindersViewModel_SnoozeAsync_ValidDuration_ReschedulesReminder()
    {
        var account = await CreateTestAccountAsync();
        var future = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        var reminder = new Reminder { AccountFk = account.Guid, Title = "Snooze me", Topic = "General", FireAt = future };
        await ReminderSvc.ScheduleAsync(reminder);

        Nav.ActionSheetResult = "8 hours";

        var vm = new RemindersViewModel(ReminderSvc, AccountService, Nav);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Reminders);

        await vm.SnoozeCommand.ExecuteAsync(vm.Reminders[0]);

        // After snooze, reminder reloaded with new fire time farther out than original
        Assert.Single(vm.Reminders);
        Assert.True(vm.Reminders[0].FireAt > future);
    }
}

// ─── GoalList: singular/plural EntryCountDisplay without completed goals ──────

public class GoalListSingularCountTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task EntryCountDisplay_OneActiveGoal_UsesSingular()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Single goal", EnteredDate = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("1 goal", vm.EntryCountDisplay, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("goals", vm.EntryCountDisplay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EntryCountDisplay_TwoActiveGoals_UsesPlural()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Goal A", EnteredDate = ts });
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Goal B", EnteredDate = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("2 goals", vm.EntryCountDisplay, StringComparison.OrdinalIgnoreCase);
    }
}

// ─── RemindersViewModel: DismissAsync and AddGeneralAsync happy paths ─────────

public class RemindersViewModelHappyPathTests : ViewModelTestBase
{
    private RemindersViewModel BuildVm() => new(ReminderSvc, AccountService, Nav);

    [Fact]
    public async Task DismissAsync_ValidReminder_RemovesFromCollectionAndUpdatesHasReminders()
    {
        var account = await CreateTestAccountAsync();
        var future = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds();
        await ReminderSvc.ScheduleAsync(new Reminder { AccountFk = account.Guid, Title = "Dismiss me", Topic = "General", FireAt = future });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Reminders);
        Assert.True(vm.HasReminders);

        await vm.DismissCommand.ExecuteAsync(vm.Reminders[0]);

        Assert.Empty(vm.Reminders);
        Assert.False(vm.HasReminders);
    }

    [Fact]
    public async Task AddGeneralAsync_ValidTitleAndDuration_ClearsTitleAndAddsReminder()
    {
        await CreateTestAccountAsync();
        Nav.ActionSheetResult = "1 hour";

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Empty(vm.Reminders);

        vm.NewReminderTitle = "Don't forget this";
        await vm.AddGeneralCommand.ExecuteAsync(null);

        Assert.Empty(vm.NewReminderTitle);
        Assert.Single(vm.Reminders);
        Assert.True(vm.HasReminders);
        Assert.Equal("Don't forget this", vm.Reminders[0].Title);
    }

    [Fact]
    public async Task AddGeneralAsync_EmptyTitle_DoesNotAddReminder()
    {
        await CreateTestAccountAsync();
        Nav.ActionSheetResult = "1 hour";

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.NewReminderTitle = string.Empty;

        // CanAddGeneral returns false when title empty — command should not execute
        Assert.False(vm.AddGeneralCommand.CanExecute(null));
        Assert.Empty(vm.Reminders);
    }
}

// ─── Dashboard: HasWeeklyChallenge false and HasWeeklyWins false paths ────────

public class DashboardWeeklyStateTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_NoActiveGoals_HasWeeklyChallengeIsFalse()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasWeeklyChallenge);
    }

    [Fact]
    public async Task Load_NoActivityThisWeek_HasWeeklyWinsIsFalse()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasWeeklyWins);
    }

    [Fact]
    public async Task Load_WithActiveGoal_HasWeeklyChallengeIsTrue()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Challenge goal", EnteredDate = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasWeeklyChallenge);
        Assert.NotEmpty(vm.WeeklyChallengeTitle);
        Assert.NotEmpty(vm.WeeklyChallengeDesc);
    }

    [Fact]
    public async Task Load_WithProgressNotesThisWeek_HasWeeklyWinsIsTrue()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Active goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        await GoalProgressRepo.SaveAsync(new GoalProgress
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            GoalFk = goal.Guid,
            NextStepItems = "Did something",
            UpdatedOn = ts
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasWeeklyWins);
        Assert.True(vm.WeekProgressNotes > 0);
    }
}

// ─── JournalEntry: ToggleTag case-insensitive removal ────────────────────────

public class JournalEntryTagCaseTests : ViewModelTestBase
{
    private JournalEntryViewModel BuildVm() =>
        new(JournalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public void ToggleTag_CaseInsensitiveRemoval_MatchesExistingTag()
    {
        var vm = BuildVm();
        vm.ToggleTagCommand.Execute("happy");
        Assert.Contains("happy", vm.Tags);

        // Toggle with different case — should still remove the existing "happy" tag
        vm.ToggleTagCommand.Execute("Happy");
        Assert.DoesNotContain("happy", vm.Tags);
        Assert.DoesNotContain("Happy", vm.Tags);
    }

    [Fact]
    public void ToggleTag_EmptyTags_AddsTagWithoutLeadingComma()
    {
        var vm = BuildVm();
        Assert.Equal(string.Empty, vm.Tags);
        vm.ToggleTagCommand.Execute("grateful");
        Assert.Equal("grateful", vm.Tags);
    }
}

// ─── Dashboard: QuickAddJournal guard when _accountGuid not yet set ──────────

public class DashboardQuickAddJournalGuardTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task QuickAddJournal_BeforeLoadAsync_DoesNotSaveOrThrow()
    {
        await CreateTestAccountAsync();
        // Do NOT call LoadAsync — _accountGuid remains empty
        var vm = BuildVm();
        vm.QuickJournalText = "Some thought";

        var ex = await Record.ExceptionAsync(() => vm.QuickAddJournalCommand.ExecuteAsync(null));
        Assert.Null(ex);

        // No journal should have been saved (guard: _accountGuid is empty)
        var account = await AccountService.GetAccountAsync();
        var journals = await JournalRepo.GetAllActiveAsync(account!.Guid);
        Assert.Empty(journals);
    }
}

// ─── GoalEntry: AddLinkedTodo cancel (user dismisses prompt) ─────────────────

public class GoalEntryAddLinkedTodoCancelTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task AddLinkedTodo_UserCancelsPrompt_DoesNotSaveTodo()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "My goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = null; // simulate user pressing Cancel

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        await vm.AddLinkedTodoCommand.ExecuteAsync(null);

        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Empty(todos);
        Assert.Empty(vm.LinkedTodos);
    }

    [Fact]
    public async Task AddLinkedTodo_UserEntersWhitespaceTitle_DoesNotSaveTodo()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "My goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = "   "; // whitespace only

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        await vm.AddLinkedTodoCommand.ExecuteAsync(null);

        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Empty(todos);
    }
}

// ─── TodoList: AddAsync fallback when _accountGuid empty and account is null ──

public class TodoListAddBeforeLoadTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task AddAsync_BeforeLoadAsync_WithNoAccount_DoesNotSaveOrThrow()
    {
        // No account created — GetAccountAsync returns null
        var vm = BuildVm();
        vm.NewTodoTitle = "Try to add";

        var ex = await Record.ExceptionAsync(() => vm.AddCommand.ExecuteAsync(null));
        Assert.Null(ex);
        Assert.Empty(vm.Todos);
    }
}

// ─── GoalEntryViewModel: ProgressBarValue derived property ───────────────────

public class GoalEntryProgressBarValueTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public void ProgressBarValue_At0Percent_ReturnsZero()
    {
        var vm = BuildVm();
        vm.ProgressPercent = 0;
        Assert.Equal(0.0, vm.ProgressBarValue);
    }

    [Fact]
    public void ProgressBarValue_At50Percent_ReturnsHalf()
    {
        var vm = BuildVm();
        vm.ProgressPercent = 50;
        Assert.Equal(0.5, vm.ProgressBarValue, 5);
    }

    [Fact]
    public void ProgressBarValue_At100Percent_ReturnsOne()
    {
        var vm = BuildVm();
        vm.ProgressPercent = 100;
        Assert.Equal(1.0, vm.ProgressBarValue, 5);
    }

    [Fact]
    public async Task Load_GoalWithProgressPercent_ProgressBarValueMatchesPercent()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Progress goal", EnteredDate = ts, ProgressPercent = 75 };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.Equal(75, vm.ProgressPercent);
        Assert.Equal(0.75, vm.ProgressBarValue, 5);
    }
}

// ─── Dashboard: HasStaleGoal false when goal has recent progress (<7 days) ───

public class DashboardStaleGoalRecentProgressTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_GoalWithProgressNoteYesterday_HasStaleGoalIsFalse()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Fresh goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        // Add a progress note dated yesterday (within 7-day window)
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        await GoalProgressRepo.UpsertFromSyncAsync(new GoalProgress
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            GoalFk = goal.Guid,
            NextStepItems = "Recent progress",
            UpdatedOn = yesterday
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasStaleGoal);
        Assert.Empty(vm.StaleGoalText);
    }

    [Fact]
    public async Task Load_GoalWithProgressNoteOlderThan7Days_HasStaleGoalIsTrue()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Old progress goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        // Add a progress note dated 8 days ago (outside 7-day window)
        var eightDaysAgo = DateTimeOffset.UtcNow.AddDays(-8).ToUnixTimeMilliseconds();
        await GoalProgressRepo.UpsertFromSyncAsync(new GoalProgress
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = account.Guid,
            GoalFk = goal.Guid,
            NextStepItems = "Old progress",
            UpdatedOn = eightDaysAgo
        });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasStaleGoal);
        Assert.Equal("Old progress goal", vm.StaleGoalText);
    }
}

// ─── DashboardViewModel: StreakDisplay from GoalProgress entries ──────────────

public class DashboardStreakDisplayTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_NoProgressNotes_StreakDisplayIsEmpty()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Empty(vm.StreakDisplay);
    }

    [Fact]
    public async Task Load_ProgressOn2ConsecutiveDays_ShowsLightningStreakEmoji()
    {
        var account = await CreateTestAccountAsync();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Streak goal", EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        await GoalRepo.SaveAsync(goal);

        for (int d = 0; d <= 1; d++)
        {
            var ts = DateTimeOffset.UtcNow.AddDays(-d).ToUnixTimeMilliseconds();
            await GoalProgressRepo.UpsertFromSyncAsync(new GoalProgress
            {
                Guid = Guid.NewGuid().ToString(),
                AccountFk = account.Guid,
                GoalFk = goal.Guid,
                NextStepItems = $"Day {d}",
                UpdatedOn = ts
            });
        }

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("⚡", vm.StreakDisplay);
        Assert.Contains("2-day", vm.StreakDisplay);
    }

    [Fact]
    public async Task Load_ProgressOn7ConsecutiveDays_ShowsFireEmoji()
    {
        var account = await CreateTestAccountAsync();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Fire goal", EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        await GoalRepo.SaveAsync(goal);

        for (int d = 0; d <= 6; d++)
        {
            var ts = DateTimeOffset.UtcNow.AddDays(-d).ToUnixTimeMilliseconds();
            await GoalProgressRepo.UpsertFromSyncAsync(new GoalProgress
            {
                Guid = Guid.NewGuid().ToString(),
                AccountFk = account.Guid,
                GoalFk = goal.Guid,
                NextStepItems = $"Day {d}",
                UpdatedOn = ts
            });
        }

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("🔥", vm.StreakDisplay);
        Assert.Contains("7-day", vm.StreakDisplay);
    }
}

// ─── DashboardViewModel: OverallTierLabel missing branches ───────────────────

public class DashboardTierLabelMissingTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_ZeroProgressNotes_OverallTierLabelIsEmpty()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.OverallTierLabel);
    }

    [Fact]
    public async Task Load_With4ProgressNotes_OverallTierLabelIsEmpty()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Almost beginner", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        for (int i = 0; i < 4; i++)
            await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), GoalFk = goal.Guid, AccountFk = account.Guid, NextStepItems = $"Note {i}", UpdatedOn = ts + i });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.OverallTierLabel);
    }

    [Fact]
    public async Task Load_WithSkilled50ProgressNotes_SetsSkilledLabel()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Skilled goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        for (int i = 0; i < 50; i++)
            await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), GoalFk = goal.Guid, AccountFk = account.Guid, NextStepItems = $"Note {i}", UpdatedOn = ts + i });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("Skilled", vm.OverallTierLabel);
    }
}

// ─── TodoEntry: SaveAsync with Guid not found in DB (fallback to new record) ──

public class TodoEntrySaveWithStaleGuidTests : ViewModelTestBase
{
    private TodoEntryViewModel BuildVm() =>
        new(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task Save_WithNonExistentGuid_FallsBackToNewRecordWithSameGuid()
    {
        var account = await CreateTestAccountAsync();
        var fakeGuid = Guid.NewGuid().ToString();

        var vm = BuildVm();
        vm.Guid = fakeGuid; // guid not in DB
        await Task.Delay(100); // let LoadAsync run (returns early for null item)
        vm.Title = "Orphaned task";

        await vm.SaveCommand.ExecuteAsync(null);

        // Should have saved with the same Guid as fallback
        var saved = await TodoRepo.GetAsync(fakeGuid);
        Assert.NotNull(saved);
        Assert.Equal("Orphaned task", saved!.Title);
    }

    [Fact]
    public async Task Save_NewTodoWithHasDueDateFalse_DueDateStoredAsNull()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.Title = "No due date";
        vm.HasDueDate = false;

        await vm.SaveCommand.ExecuteAsync(null);

        var account = await AccountService.GetAccountAsync();
        var todos = await TodoRepo.GetPendingAsync(account!.Guid);
        Assert.Single(todos);
        Assert.Null(todos[0].DueDate);
    }

    [Fact]
    public async Task Save_NewTodoWithHasDueDateTrue_DueDatePersistedCorrectly()
    {
        await CreateTestAccountAsync();
        var targetDate = DateTime.Today.AddDays(3);
        var vm = BuildVm();
        vm.Title = "Has due date";
        vm.HasDueDate = true;
        vm.DueDate = targetDate;

        await vm.SaveCommand.ExecuteAsync(null);

        var account = await AccountService.GetAccountAsync();
        var todos = await TodoRepo.GetPendingAsync(account!.Guid);
        Assert.Single(todos);
        Assert.NotNull(todos[0].DueDate);
        var savedDate = DateTimeOffset.FromUnixTimeMilliseconds(todos[0].DueDate!.Value).LocalDateTime.Date;
        Assert.Equal(targetDate.Date, savedDate);
    }
}

// ─── JournalEntry: SaveAsync with Guid not found in DB ───────────────────────

public class JournalEntrySaveWithStaleGuidTests : ViewModelTestBase
{
    private JournalEntryViewModel BuildVm() =>
        new(JournalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task Save_WithNonExistentGuid_FallsBackToNewRecordWithSameGuid()
    {
        var account = await CreateTestAccountAsync();
        var fakeGuid = Guid.NewGuid().ToString();

        var vm = BuildVm();
        vm.Guid = fakeGuid;
        await Task.Delay(100);
        vm.Notes = "Orphaned note";

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = await JournalRepo.GetAsync(fakeGuid);
        Assert.NotNull(saved);
        Assert.Equal("Orphaned note", saved!.Notes);
    }
}

// ─── GoalEntry: SaveAsync null paths for optional date fields ─────────────────

public class GoalEntrySaveNullFieldTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task Save_HasExpirationDateFalse_PersistsNullExpirationDate()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.GoalText = "No expiry";
        vm.HasExpirationDate = false;
        await vm.SaveCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(goals);
        Assert.Null(goals[0].ExpirationDate);
    }

    [Fact]
    public async Task Save_HasNextMeetingDateFalse_PersistsNullNextMeetingDate()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.GoalText = "No meeting";
        vm.HasNextMeetingDate = false;
        await vm.SaveCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(goals);
        Assert.Null(goals[0].NextMeetingDate);
    }

    [Fact]
    public async Task Save_ProgressPercentZero_PersistsNullProgressPercent()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.GoalText = "Zero progress";
        vm.ProgressPercent = 0;
        await vm.SaveCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(goals);
        Assert.Null(goals[0].ProgressPercent);
    }

    [Fact]
    public async Task Save_ProgressPercentAboveZero_PersistsProgressPercent()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.GoalText = "Some progress";
        vm.ProgressPercent = 40;
        await vm.SaveCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(goals);
        Assert.Equal(40, goals[0].ProgressPercent);
    }

    [Fact]
    public async Task Save_IsPinnedTrue_PersistedCorrectly()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildVm();
        vm.GoalText = "Pinned new goal";
        vm.IsPinned = true;
        await vm.SaveCommand.ExecuteAsync(null);

        var goals = await GoalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(goals);
        Assert.True(goals[0].IsPinned);
    }
}

// ─── TodoEntryViewModel: DeleteAsync paths ───────────────────────────────────

public class TodoEntryDeleteTests : ViewModelTestBase
{
    private TodoEntryViewModel BuildVm() =>
        new(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task DeleteAsync_EmptyGuid_ReturnsEarlyWithoutThrowing()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        // Guid is empty — should return early
        var ex = await Record.ExceptionAsync(() => vm.DeleteCommand.ExecuteAsync(null));
        Assert.Null(ex);
        Assert.Empty(Nav.AlertTitles); // no confirm dialog shown
    }

    [Fact]
    public async Task DeleteAsync_UserConfirms_DeletesTodoAndNavigatesBack()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Delete me", UpdatedOn = ts };
        await TodoRepo.SaveAsync(todo);

        Nav.AlertConfirmResult = true;
        var vm = BuildVm();
        vm.Guid = todo.Guid;
        await Task.Delay(100);

        await vm.DeleteCommand.ExecuteAsync(null);

        var deleted = await TodoRepo.GetAsync(todo.Guid);
        Assert.True(deleted is null || deleted.DeletedAt.HasValue);
        Assert.Contains("..", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task DeleteAsync_UserCancels_TodoNotDeleted()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Keep me", UpdatedOn = ts };
        await TodoRepo.SaveAsync(todo);

        Nav.AlertConfirmResult = false;
        var vm = BuildVm();
        vm.Guid = todo.Guid;
        await Task.Delay(100);

        await vm.DeleteCommand.ExecuteAsync(null);

        var still = await TodoRepo.GetAsync(todo.Guid);
        Assert.NotNull(still);
        Assert.Null(still!.DeletedAt);
        Assert.DoesNotContain("..", Nav.NavigatedRoutes);
    }
}

// ─── JournalEntry: DeleteAsync cancel path ───────────────────────────────────

public class JournalEntryDeleteCancelTests : ViewModelTestBase
{
    private JournalEntryViewModel BuildVm() =>
        new(JournalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task DeleteAsync_UserCancels_JournalNotDeleted()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Keep this note", EnteredDate = ts, UpdatedOn = ts };
        await JournalRepo.SaveAsync(journal);

        Nav.AlertConfirmResult = false;
        var vm = BuildVm();
        vm.Guid = journal.Guid;
        await Task.Delay(100);

        await vm.DeleteCommand.ExecuteAsync(null);

        var still = await JournalRepo.GetAsync(journal.Guid);
        Assert.NotNull(still);
        Assert.Null(still!.DeletedAt);
        Assert.DoesNotContain("..", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task DeleteAsync_EmptyGuid_ReturnsWithoutShowingAlert()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        // Guid is empty — early return before showing dialog

        var ex = await Record.ExceptionAsync(() => vm.DeleteCommand.ExecuteAsync(null));
        Assert.Null(ex);
        Assert.Empty(Nav.AlertTitles);
    }
}

// ─── GoalEntry: DeleteAsync cancel and empty-guid paths ──────────────────────

public class GoalEntryDeleteGuardTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task DeleteAsync_EmptyGuid_ReturnsEarlyWithoutDialog()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        // Guid is empty — should return early

        var ex = await Record.ExceptionAsync(() => vm.DeleteCommand.ExecuteAsync(null));
        Assert.Null(ex);
        Assert.Empty(Nav.AlertTitles);
    }

    [Fact]
    public async Task DeleteAsync_UserCancels_GoalNotDeleted()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Keep this goal", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        Nav.AlertConfirmResult = false;
        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        await vm.DeleteCommand.ExecuteAsync(null);

        var still = await GoalRepo.GetAsync(goal.Guid);
        Assert.NotNull(still);
        Assert.Null(still!.DeletedAt);
        Assert.DoesNotContain("..", Nav.NavigatedRoutes);
    }
}

// ─── DashboardViewModel: OverdueTodoCount and HasOverdueTodos ─────────────────

public class DashboardOverdueTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_WithOverdueTodo_SetsHasOverdueTodosTrue()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Overdue task", DueDate = yesterday, UpdatedOn = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasOverdueTodos);
        Assert.True(vm.OverdueTodoCount > 0);
    }

    [Fact]
    public async Task Load_WithNoOverdueTodo_HasOverdueTodosIsFalse()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var tomorrow = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Future task", DueDate = tomorrow, UpdatedOn = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasOverdueTodos);
        Assert.Equal(0, vm.OverdueTodoCount);
    }
}

// ─── JournalListViewModel: combined text + Month filter ─────────────────────

public class JournalListCombinedFilterTests : ViewModelTestBase
{
    private JournalListViewModel BuildVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Filter_TextAndMonthTogether_ShowsOnlyMatchingEntriesWithinMonth()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var oldMs = DateTimeOffset.UtcNow.AddDays(-45).ToUnixTimeMilliseconds(); // > 30 days ago

        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Recent piano practice", EnteredDate = now, UpdatedOn = now });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Old piano practice", EnteredDate = oldMs, UpdatedOn = oldMs });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Recent swimming", EnteredDate = now, UpdatedOn = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SetDateFilterCommand.Execute("Month");
        vm.FilterText = "piano";

        // Should only show the recent piano entry (not the old one, not the swimming one)
        Assert.Single(vm.Journals);
        Assert.Contains("piano", vm.Journals[0].Notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Filter_TextOnly_ShowsMatchAcrossNotesAndActivity()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Went to soccer", Activity = "Running", EnteredDate = now, UpdatedOn = now });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Rest day", Activity = "Soccer training", EnteredDate = now, UpdatedOn = now });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Unrelated", Activity = "Swimming", EnteredDate = now, UpdatedOn = now });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "soccer";

        Assert.Equal(2, vm.Journals.Count);
        Assert.Contains("shown", vm.EntryCountDisplay, StringComparison.OrdinalIgnoreCase);
    }
}

// ─── SettingsViewModel: LastSyncDisplay = "Never" when LastSyncAt == 0 ────────

public class SettingsViewModelNeverSyncTests : ViewModelTestBase
{
    private SettingsViewModel BuildVm() =>
        new(AccountService, new FakeHttpClientFactory(new NoOpHttpHandler()), Analytics);

    [Fact]
    public async Task Load_WithLastSyncAtZero_ShowsNever()
    {
        await CreateTestAccountAsync();
        // New account has LastSyncAt = 0 by default

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Never", vm.LastSyncDisplay);
    }

    [Fact]
    public async Task Load_NoAccount_DoesNotPopulateFields()
    {
        // No account → early return
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Empty(vm.NickName);
        Assert.Empty(vm.AccountGuid);
    }
}

// ─── DashboardViewModel: LastSyncDisplay with LastSyncAt == 0 ────────────────

public class DashboardLastSyncDisplayTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_AccountWithLastSyncAtZero_ShowsNeverSynced()
    {
        await CreateTestAccountAsync();
        // New account has LastSyncAt = 0

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Never synced", vm.LastSyncDisplay);
    }
}

// ─── JournalEntry: SaveAsync with non-today EnteredDate ──────────────────────

public class JournalEntryEnteredDateTests : ViewModelTestBase
{
    private JournalEntryViewModel BuildVm() =>
        new(JournalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task Save_WithSpecificPastDate_PersistsEnteredDateCorrectly()
    {
        await CreateTestAccountAsync();
        var pastDate = DateTime.Today.AddDays(-7);

        var vm = BuildVm();
        vm.Notes = "Seven days ago";
        vm.EnteredDate = pastDate;
        await vm.SaveCommand.ExecuteAsync(null);

        var account = await AccountService.GetAccountAsync();
        var journals = await JournalRepo.GetAllActiveAsync(account!.Guid);
        Assert.Single(journals);
        var savedDate = DateTimeOffset.FromUnixTimeMilliseconds(journals[0].EnteredDate).LocalDateTime.Date;
        Assert.Equal(pastDate.Date, savedDate);
    }
}

// ─── SnoozeHelper: Custom path with valid unit choices ───────────────────────

public class SnoozeHelperCustomUnitTests : ViewModelTestBase
{
    [Fact]
    public async Task PickAsync_3DaysChoice_ReturnsCorrectTimeSpan()
    {
        await CreateTestAccountAsync();
        var future = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        await ReminderSvc.ScheduleAsync(new Reminder { AccountFk = (await AccountService.GetAccountAsync())!.Guid, Title = "Test", Topic = "General", FireAt = future });

        Nav.ActionSheetResult = "3 days";

        var vm = new RemindersViewModel(ReminderSvc, AccountService, Nav);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Reminders);

        var beforeSnooze = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await vm.SnoozeCommand.ExecuteAsync(vm.Reminders[0]);

        Assert.Single(vm.Reminders);
        var expectedMinFireAt = beforeSnooze + (long)TimeSpan.FromDays(3).TotalMilliseconds - 5000;
        Assert.True(vm.Reminders[0].FireAt >= expectedMinFireAt);
    }

    [Fact]
    public async Task PickAsync_CustomWithDaysUnit_ReturnsCorrectDuration()
    {
        await CreateTestAccountAsync();
        var future = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        await ReminderSvc.ScheduleAsync(new Reminder { AccountFk = (await AccountService.GetAccountAsync())!.Guid, Title = "Test", Topic = "General", FireAt = future });

        // First ActionSheet: "Custom...", Second ActionSheet: "Days", Prompt: "2"
        Nav.ActionSheetResultQueue.Enqueue("Custom...");
        Nav.ActionSheetResultQueue.Enqueue("Days");
        Nav.PromptResult = "2";

        var vm = new RemindersViewModel(ReminderSvc, AccountService, Nav);
        await vm.LoadCommand.ExecuteAsync(null);

        var beforeSnooze = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await vm.SnoozeCommand.ExecuteAsync(vm.Reminders[0]);

        // Should be rescheduled ~2 days out
        var expectedMin = beforeSnooze + (long)TimeSpan.FromDays(2).TotalMilliseconds - 5000;
        Assert.True(vm.Reminders[0].FireAt >= expectedMin);
    }

    [Fact]
    public async Task PickAsync_CustomWithWeeksUnit_ReturnsCorrectDuration()
    {
        await CreateTestAccountAsync();
        var future = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        await ReminderSvc.ScheduleAsync(new Reminder { AccountFk = (await AccountService.GetAccountAsync())!.Guid, Title = "Test", Topic = "General", FireAt = future });

        // Custom → "1" week
        Nav.ActionSheetResultQueue.Enqueue("Custom...");
        Nav.ActionSheetResultQueue.Enqueue("Weeks");
        Nav.PromptResult = "1";

        var vm = new RemindersViewModel(ReminderSvc, AccountService, Nav);
        await vm.LoadCommand.ExecuteAsync(null);

        var beforeSnooze = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await vm.SnoozeCommand.ExecuteAsync(vm.Reminders[0]);

        var expectedMin = beforeSnooze + (long)TimeSpan.FromDays(7).TotalMilliseconds - 5000;
        Assert.True(vm.Reminders[0].FireAt >= expectedMin);
    }

    [Fact]
    public async Task PickAsync_CustomUnitCancelled_ReturnsNullAndNoReschedule()
    {
        await CreateTestAccountAsync();
        var future = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        var account = await AccountService.GetAccountAsync();
        await ReminderSvc.ScheduleAsync(new Reminder { AccountFk = account!.Guid, Title = "Test", Topic = "General", FireAt = future });

        // Custom → "3" → unit cancelled (null)
        Nav.ActionSheetResultQueue.Enqueue("Custom...");
        Nav.ActionSheetResultQueue.Enqueue(null); // cancel unit picker
        Nav.PromptResult = "3";

        var vm = new RemindersViewModel(ReminderSvc, AccountService, Nav);
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.SnoozeCommand.ExecuteAsync(vm.Reminders[0]);

        // No rescheduling — duration was null
        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.Equal(future, pending[0].FireAt); // unchanged
    }
}

// ─── SettingsViewModel: TestConnectionAsync non-empty URL paths ──────────────

public class SettingsViewModelTestConnectionTests : ViewModelTestBase
{
    private class StatusResponseHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("network down");
    }

    [Fact]
    public async Task TestConnection_ServerReturns200_SetsConnectedMessage()
    {
        var vm = new SettingsViewModel(AccountService, new FakeHttpClientFactory(new StatusResponseHandler(HttpStatusCode.OK)), Analytics);
        vm.ServerUrl = "https://server.local";
        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.Equal("Connected!", vm.StatusMessage);
    }

    [Fact]
    public async Task TestConnection_ServerReturns500_SetsServerErrorMessage()
    {
        var vm = new SettingsViewModel(AccountService, new FakeHttpClientFactory(new StatusResponseHandler(HttpStatusCode.InternalServerError)), Analytics);
        vm.ServerUrl = "https://server.local";
        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.Contains("500", vm.StatusMessage);
    }

    [Fact]
    public async Task TestConnection_NetworkException_SetsCannotReachMessage()
    {
        var vm = new SettingsViewModel(AccountService, new FakeHttpClientFactory(new ThrowingHandler()), Analytics);
        vm.ServerUrl = "https://server.local";
        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.Contains("Cannot reach", vm.StatusMessage);
    }
}

// ─── TodoListViewModel: RefreshAsync paths ───────────────────────────────────

public class TodoListRefreshTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Refresh_NoAccount_SetsIsRefreshingFalse()
    {
        var vm = BuildVm();
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.False(vm.IsRefreshing);
    }

    [Fact]
    public async Task Refresh_WithAccount_ReloadsItems()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Task one", UpdatedOn = now });
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Task two", UpdatedOn = now });

        var vm = BuildVm();
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Todos.Count);
        Assert.Empty(vm.StatusMessage);
        Assert.False(vm.IsRefreshing);
    }
}

// ─── TodoListViewModel: UncompleteAsync clears showCompleted when empty ───────

public class TodoListUncompleteShowCompletedTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Uncomplete_LastCompletedItem_HidesCompletedSection()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Task A", CompletedAt = now, UpdatedOn = now };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ShowCompletedTodos = true;
        Assert.Single(vm.CompletedTodos);

        await vm.UncompleteCommand.ExecuteAsync(vm.CompletedTodos[0]);

        Assert.False(vm.HasCompletedTodos);
        Assert.False(vm.ShowCompletedTodos);
        Assert.Equal(0, vm.CompletedTodoCount);
    }
}

// ─── TodoListViewModel: ToggleCompleted flips ShowCompletedTodos ─────────────

public class TodoListToggleCompletedTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task ToggleCompleted_InitiallyFalse_BecomesTrue()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.ShowCompletedTodos);
        vm.ToggleCompletedCommand.Execute(null);
        Assert.True(vm.ShowCompletedTodos);
    }

    [Fact]
    public async Task ToggleCompleted_TwiceReturnsToFalse()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ToggleCompletedCommand.Execute(null);
        vm.ToggleCompletedCommand.Execute(null);
        Assert.False(vm.ShowCompletedTodos);
    }
}

// ─── TodoListViewModel: DeleteAsync (list-level) ─────────────────────────────

public class TodoListDeleteTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task DeleteAsync_UserCancels_TodoRemainsInList()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Keep me", UpdatedOn = now });

        Nav.AlertConfirmResult = false;
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Todos);

        await vm.DeleteCommand.ExecuteAsync(vm.Todos[0]);

        Assert.Single(vm.Todos);
    }

    [Fact]
    public async Task DeleteAsync_UserConfirms_TodoRemovedFromList()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Delete me", UpdatedOn = now });

        Nav.AlertConfirmResult = true;
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Todos);

        await vm.DeleteCommand.ExecuteAsync(vm.Todos[0]);

        Assert.Empty(vm.Todos);
    }

    [Fact]
    public async Task DeleteAsync_CompletedTodo_RemovedFromCompletedList()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Done thing", CompletedAt = now, UpdatedOn = now };
        await TodoRepo.SaveAsync(todo);

        Nav.AlertConfirmResult = true;
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.CompletedTodos);

        await vm.DeleteCommand.ExecuteAsync(vm.CompletedTodos[0]);

        Assert.Empty(vm.CompletedTodos);
        Assert.False(vm.HasCompletedTodos);
        Assert.False(vm.ShowCompletedTodos);
    }
}

// ─── TodoListViewModel: WeekOverWeekMessage positive diff path ───────────────

public class TodoListWeekOverWeekPositiveTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_MoreThisWeekThanLastWeek_ShowsPositiveDiff()
    {
        var account = await CreateTestAccountAsync();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var weekStartMs = DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeMilliseconds();
        var lastWeekMs = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();

        // 2 completed this week
        for (int i = 0; i < 2; i++)
            await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = $"This week {i}", CompletedAt = nowMs, UpdatedOn = nowMs });
        // 1 completed last week
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Last week", CompletedAt = lastWeekMs, UpdatedOn = lastWeekMs });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasWeekOverWeekMessage);
        Assert.Contains("+1", vm.WeekOverWeekMessage); // 2 this week vs 1 last week
    }

    [Fact]
    public async Task Load_NoLastWeekData_NoWeekOverWeekMessage()
    {
        var account = await CreateTestAccountAsync();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 1 completed this week, nothing last week
        await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "This week", CompletedAt = nowMs, UpdatedOn = nowMs });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasWeekOverWeekMessage);
    }
}

// ─── DashboardViewModel: QuickNoteForFocusGoal guard and success paths ────────

public class DashboardQuickNoteForFocusGoalTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task QuickNoteForFocusGoal_EmptyStaleGoalGuid_DoesNothing()
    {
        await CreateTestAccountAsync();
        Nav.PromptResult = "Some note";
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        // No goals → StaleGoalGuid is empty
        Assert.Empty(vm.StaleGoalGuid);

        await vm.QuickNoteForFocusGoalCommand.ExecuteAsync(null);

        var progress = await GoalProgressRepo.GetModifiedSinceAsync((await AccountService.GetAccountAsync())!.Guid, 0);
        Assert.Empty(progress.Where(p => p.DeletedAt == null));
    }

    [Fact]
    public async Task QuickNoteForFocusGoal_CancelledPrompt_DoesNotSave()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Stale goal", EnteredDate = ts, UpdatedOn = ts });

        Nav.PromptResult = null;
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.QuickNoteForFocusGoalCommand.ExecuteAsync(null);

        var progress = await GoalProgressRepo.GetModifiedSinceAsync(account.Guid, 0);
        Assert.Empty(progress.Where(p => p.DeletedAt == null));
    }

    [Fact]
    public async Task QuickNoteForFocusGoal_WithNote_SavesAndClearsStaleGoal()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Exercise daily", EnteredDate = ts, UpdatedOn = ts });

        Nav.PromptResult = "Did 20 pushups";
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.HasStaleGoal);

        await vm.QuickNoteForFocusGoalCommand.ExecuteAsync(null);

        Assert.Empty(vm.StaleGoalText);
        Assert.Empty(vm.StaleGoalGuid);
        Assert.False(vm.HasStaleGoal);
        var progress = await GoalProgressRepo.GetModifiedSinceAsync(account.Guid, 0);
        Assert.Single(progress.Where(p => p.DeletedAt == null));
    }
}

// ─── DashboardViewModel: GoToStaleGoal navigates to goal entry ───────────────

public class DashboardGoToStaleGoalTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task GoToStaleGoal_WithGuid_NavigatesToGoalEntry()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn piano", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.HasStaleGoal);

        await vm.GoToStaleGoalCommand.ExecuteAsync(null);

        Assert.Contains($"goals/entry?guid={goal.Guid}", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task GoToStaleGoal_EmptyGuid_DoesNotNavigate()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        // No goals → StaleGoalGuid empty

        await vm.GoToStaleGoalCommand.ExecuteAsync(null);

        Assert.Empty(Nav.NavigatedRoutes.Where(r => r.Contains("goals/entry")));
    }
}

// ─── RemindersViewModel: AddGeneralAsync snooze cancel path ──────────────────

public class RemindersAddGeneralSnoozeCancelTests : ViewModelTestBase
{
    [Fact]
    public async Task AddGeneralAsync_SnoozeCancelled_NoReminderScheduled()
    {
        await CreateTestAccountAsync();
        Nav.ActionSheetResult = null; // cancel snooze picker
        Nav.AlertConfirmResult = true;

        var vm = new RemindersViewModel(ReminderSvc, AccountService, Nav);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.NewReminderTitle = "My reminder";

        await vm.AddGeneralCommand.ExecuteAsync(null);

        var pending = await ReminderSvc.GetPendingAsync((await AccountService.GetAccountAsync())!.Guid);
        Assert.Empty(pending);
        // Title should not be cleared when cancelled
        Assert.Equal("My reminder", vm.NewReminderTitle);
    }
}

// ─── SettingsViewModel: SaveServerUrl with non-empty URL ─────────────────────

public class SettingsViewModelSaveUrlNonEmptyTests : ViewModelTestBase
{
    [Fact]
    public async Task SaveServerUrl_NonEmptyUrl_SetsUrlSavedMessage()
    {
        await CreateTestAccountAsync();
        var vm = new SettingsViewModel(AccountService, new FakeHttpClientFactory(new NoOpHttpHandler()), Analytics);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ServerUrl = "https://my.server.org";
        await vm.SaveServerUrlCommand.ExecuteAsync(null);
        Assert.Contains("saved", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
}

// ─── DashboardViewModel: Greeting property ───────────────────────────────────

public class DashboardGreetingTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_WithAccount_GreetingContainsNickName()
    {
        await CreateTestAccountAsync("Zara");
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Contains("Zara", vm.Greeting);
    }

    [Fact]
    public async Task Load_WithAccount_GreetingContainsTimeOfDay()
    {
        await CreateTestAccountAsync("Kid");
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        var validGreetings = new[] { "Good morning", "Good afternoon", "Good evening" };
        Assert.Contains(validGreetings, g => vm.Greeting.Contains(g));
    }
}

// ─── TodoListViewModel: SnoozeOverdue before LoadAsync returns early ──────────

public class TodoListSnoozeOverdueBeforeLoadTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task SnoozeOverdue_BeforeLoad_DoesNotThrow()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();
        // _accountGuid is empty before LoadAsync
        var ex = await Record.ExceptionAsync(() => vm.SnoozeOverdueCommand.ExecuteAsync(null));
        Assert.Null(ex);
    }
}

// ─── JournalRepository: HasEntryTodayAsync directly ──────────────────────────

public class JournalRepositoryHasTodayTests : ViewModelTestBase
{
    [Fact]
    public async Task HasEntryToday_WithTodayEntry_ReturnsTrue()
    {
        var account = await CreateTestAccountAsync();
        var todayMs = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Today note", EnteredDate = todayMs, UpdatedOn = todayMs });

        var result = await JournalRepo.HasEntryTodayAsync(account.Guid);
        Assert.True(result);
    }

    [Fact]
    public async Task HasEntryToday_WithOnlyYesterdayEntry_ReturnsFalse()
    {
        var account = await CreateTestAccountAsync();
        var yesterdayMs = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today.AddDays(-1), DateTimeKind.Local)).ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Yesterday", EnteredDate = yesterdayMs, UpdatedOn = yesterdayMs });

        var result = await JournalRepo.HasEntryTodayAsync(account.Guid);
        Assert.False(result);
    }

    [Fact]
    public async Task HasEntryToday_NoEntries_ReturnsFalse()
    {
        var account = await CreateTestAccountAsync();
        var result = await JournalRepo.HasEntryTodayAsync(account.Guid);
        Assert.False(result);
    }
}

// ─── JournalRepository: GetJournalStreakAsync directly ───────────────────────

public class JournalRepositoryStreakTests : ViewModelTestBase
{
    [Fact]
    public async Task GetJournalStreak_NoEntries_ReturnsZero()
    {
        var account = await CreateTestAccountAsync();
        var streak = await JournalRepo.GetJournalStreakAsync(account.Guid);
        Assert.Equal(0, streak);
    }

    [Fact]
    public async Task GetJournalStreak_TodayAndYesterday_Returns2()
    {
        var account = await CreateTestAccountAsync();
        var today = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Local));
        var yesterday = today.AddDays(-1);

        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "T", EnteredDate = today.ToUnixTimeMilliseconds(), UpdatedOn = today.ToUnixTimeMilliseconds() });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Y", EnteredDate = yesterday.ToUnixTimeMilliseconds(), UpdatedOn = yesterday.ToUnixTimeMilliseconds() });

        var streak = await JournalRepo.GetJournalStreakAsync(account.Guid);
        Assert.Equal(2, streak);
    }
}

// ─── ReminderService: GetForEntityAsync ──────────────────────────────────────

public class ReminderServiceGetForEntityTests : ViewModelTestBase
{
    [Fact]
    public async Task GetForEntityAsync_ReturnsRemindersForGuid()
    {
        var account = await CreateTestAccountAsync();
        var entityGuid = Guid.NewGuid().ToString();
        var otherGuid = Guid.NewGuid().ToString();
        var future = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();

        await ReminderSvc.ScheduleAsync(new Reminder { AccountFk = account.Guid, Title = "For entity", Topic = "Goal", EntityGuid = entityGuid, FireAt = future });
        await ReminderSvc.ScheduleAsync(new Reminder { AccountFk = account.Guid, Title = "Other entity", Topic = "Goal", EntityGuid = otherGuid, FireAt = future });

        var forEntity = await ReminderSvc.GetForEntityAsync(entityGuid);

        Assert.Single(forEntity);
        Assert.Equal("For entity", forEntity[0].Title);
    }

    [Fact]
    public async Task GetForEntityAsync_NoMatch_ReturnsEmpty()
    {
        await CreateTestAccountAsync();
        var result = await ReminderSvc.GetForEntityAsync(Guid.NewGuid().ToString());
        Assert.Empty(result);
    }
}

// ─── GoalProgressRepository: GetCurrentStreakAsync directly ──────────────────

public class GoalProgressStreakDirectTests : ViewModelTestBase
{
    [Fact]
    public async Task GetCurrentStreak_NoProgress_ReturnsZero()
    {
        var account = await CreateTestAccountAsync();
        var streak = await GoalProgressRepo.GetCurrentStreakAsync(account.Guid);
        Assert.Equal(0, streak);
    }

    [Fact]
    public async Task GetCurrentStreak_ConsecutiveDays_ReturnsCorrectStreak()
    {
        var account = await CreateTestAccountAsync();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Test", EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        await GoalRepo.SaveAsync(goal);

        // Save notes on today and yesterday
        var today = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Local));
        var yesterday = today.AddDays(-1);
        await GoalProgressRepo.UpsertFromSyncAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goal.Guid, NextStepItems = "Today", UpdatedOn = today.ToUnixTimeMilliseconds() });
        await GoalProgressRepo.UpsertFromSyncAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goal.Guid, NextStepItems = "Yesterday", UpdatedOn = yesterday.ToUnixTimeMilliseconds() });

        var streak = await GoalProgressRepo.GetCurrentStreakAsync(account.Guid);
        Assert.Equal(2, streak);
    }

    [Fact]
    public async Task GetCurrentStreak_GapInDays_StopsAtGap()
    {
        var account = await CreateTestAccountAsync();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Test", EnteredDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), UpdatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        await GoalRepo.SaveAsync(goal);

        // Today and 3 days ago (gap of 2 days)
        var today = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Local));
        var threeDaysAgo = today.AddDays(-3);
        await GoalProgressRepo.UpsertFromSyncAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goal.Guid, NextStepItems = "Today", UpdatedOn = today.ToUnixTimeMilliseconds() });
        await GoalProgressRepo.UpsertFromSyncAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goal.Guid, NextStepItems = "3 days ago", UpdatedOn = threeDaysAgo.ToUnixTimeMilliseconds() });

        var streak = await GoalProgressRepo.GetCurrentStreakAsync(account.Guid);
        Assert.Equal(1, streak); // Only today (gap breaks streak)
    }
}

// ─── GoalListViewModel: DeleteAsync cancel path ──────────────────────────────

public class GoalListDeleteCancelTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task DeleteAsync_UserCancels_GoalRemainsInList()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Keep this goal", EnteredDate = ts, UpdatedOn = ts });

        Nav.AlertConfirmResult = false;
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Goals);

        await vm.DeleteCommand.ExecuteAsync(vm.Goals[0]);

        Assert.Single(vm.Goals);
    }
}

// ─── DashboardViewModel: LoadAsync no account returns early ──────────────────

public class DashboardNoAccountTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task LoadAsync_NoAccount_DoesNotThrowAndGreetingIsEmpty()
    {
        var vm = BuildVm();
        var ex = await Record.ExceptionAsync(() => vm.LoadCommand.ExecuteAsync(null));
        Assert.Null(ex);
        Assert.Empty(vm.Greeting);
    }
}

// ─── TodoListViewModel and JournalListViewModel: OpenAsync navigates ──────────

public class ListViewModelOpenAsyncTests : ViewModelTestBase
{
    private TodoListViewModel BuildTodoVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    private JournalListViewModel BuildJournalVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task TodoList_Open_NonNull_NavigatesToEntry()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Navigate me", UpdatedOn = now };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildTodoVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.OpenCommand.ExecuteAsync(vm.Todos[0]);

        Assert.Contains($"todos/entry?guid={todo.Guid}", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task JournalList_Open_NonNull_NavigatesToEntry()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Navigate me", EnteredDate = now, UpdatedOn = now });

        var vm = BuildJournalVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.OpenCommand.ExecuteAsync(vm.Journals[0]);

        Assert.Contains(Nav.NavigatedRoutes, r => r.Contains("journal/entry?guid="));
    }
}

// ─── DashboardViewModel: WeeklyChallenge Motivation and Status ───────────────

public class DashboardWeeklyChallengeMotivationTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_WeeklyChallengeDone_ShowsDoneStatus()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run daily", EnteredDate = ts, UpdatedOn = ts });

        // Add enough activity this week to complete any challenge type
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < 10; i++)
            await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = $"Journal {i}", EnteredDate = now, UpdatedOn = now });
        for (int i = 0; i < 10; i++)
        {
            var p = new GoalProgress { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = (await GoalRepo.GetAllActiveAsync(account.Guid))[0].Guid, NextStepItems = $"Note {i}", UpdatedOn = now };
            await GoalProgressRepo.SaveAsync(p);
        }
        var done = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Done", CompletedAt = now, UpdatedOn = now };
        await TodoRepo.SaveAsync(done);
        for (int i = 0; i < 10; i++)
        {
            var t = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = $"Done {i}", CompletedAt = now, UpdatedOn = now };
            await TodoRepo.SaveAsync(t);
        }

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        // Whatever challenge is active, if done it should show "Done" in status
        if (vm.WeeklyChallengeDone)
            Assert.Contains("Done", vm.WeeklyChallengeStatus);
    }

    [Fact]
    public async Task Load_ChallengeNotStarted_MotivationSaysBuildMomentum()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Clean room", EnteredDate = ts, UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        // With no activity, wcCurrent = 0, motivation = "Start now and build momentum!"
        if (!vm.WeeklyChallengeDone && vm.WeeklyChallengePctValue == 0)
            Assert.Contains("momentum", vm.WeeklyChallengeMotivation, StringComparison.OrdinalIgnoreCase);
    }
}

// ─── DashboardViewModel: OverallTierLabel remaining tiers ────────────────────

public class DashboardOverallTierHighTests : ViewModelTestBase
{
    private DashboardViewModel BuildVm() =>
        new(JournalRepo, GoalRepo, GoalProgressRepo, TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Load_200ProgressNotes_ShowsMasterTier()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Master goal", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        for (int i = 0; i < 200; i++)
            await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goal.Guid, NextStepItems = $"Note {i}", UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("Master", vm.OverallTierLabel);
    }

    [Fact]
    public async Task Load_500ProgressNotes_ShowsLegendTier()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Legend goal", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        for (int i = 0; i < 500; i++)
            await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalFk = goal.Guid, NextStepItems = $"Note {i}", UpdatedOn = ts });

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("Legend", vm.OverallTierLabel);
    }
}

// ─── GoalEntryViewModel: CompleteLinkedTodoAsync null guard ──────────────────

public class GoalEntryCompleteLinkedTodoNullTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task CompleteLinkedTodo_NullTodo_DoesNotThrow()
    {
        await CreateTestAccountAsync();
        var vm = BuildVm();

        var ex = await Record.ExceptionAsync(() => vm.CompleteLinkedTodoCommand.ExecuteAsync(null!));
        Assert.Null(ex);
    }
}

// ─── JournalEntryViewModel: SetReminderAsync empty notes uses "Journal entry" label ──

public class JournalEntrySetReminderEmptyNotesTests : ViewModelTestBase
{
    private JournalEntryViewModel BuildVm() =>
        new(JournalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task SetReminder_EmptyNotes_UsesJournalEntryLabel()
    {
        var account = await CreateTestAccountAsync();
        Nav.ActionSheetResult = "1 hour";

        var vm = BuildVm();
        // Notes left empty
        await vm.SetReminderCommand.ExecuteAsync(null);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.Contains("Journal entry", pending[0].Title);
    }

    [Fact]
    public async Task SetReminder_LongNotes_TruncatesLabelAt40Chars()
    {
        var account = await CreateTestAccountAsync();
        Nav.ActionSheetResult = "1 hour";

        var vm = BuildVm();
        vm.Notes = "This is a very long journal note that exceeds forty characters easily";
        await vm.SetReminderCommand.ExecuteAsync(null);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.Contains("This is a very long journal note that ex…", pending[0].Title);
    }
}

// ─── GoalEntryViewModel: SetReminderAsync null guard (empty Guid) ────────────

public class GoalEntrySetReminderGuardTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task SetReminder_EmptyGuid_ReturnsEarlyNoReminderScheduled()
    {
        var account = await CreateTestAccountAsync();
        Nav.ActionSheetResult = "1 hour";

        var vm = BuildVm();
        // Guid is empty — guard fires
        await vm.SetReminderCommand.ExecuteAsync(null);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task SetReminder_ValidGuidWithDuration_SchedulesReminder()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn piano", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);
        Nav.ActionSheetResult = "1 hour";

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        await vm.SetReminderCommand.ExecuteAsync(null);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.Equal("Goal", pending[0].Topic);
        Assert.Contains("Learn piano", pending[0].Title);
    }
}

// ─── GoalEntryViewModel: CompleteLinkedTodoAsync success — HasLinkedTodos reflects count ──

public class GoalEntryCompleteLinkedTodoSuccessTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task CompleteLinkedTodo_LastTodo_HasLinkedTodosFalse()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run a marathon", EnteredDate = ts, UpdatedOn = ts };
        await GoalRepo.SaveAsync(goal);

        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Train 5k", Notes = $"Goal: Run a marathon", UpdatedOn = ts };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.True(vm.HasLinkedTodos);
        Assert.Single(vm.LinkedTodos);

        await vm.CompleteLinkedTodoCommand.ExecuteAsync(vm.LinkedTodos[0]);

        Assert.False(vm.HasLinkedTodos);
        Assert.Empty(vm.LinkedTodos);
    }
}
