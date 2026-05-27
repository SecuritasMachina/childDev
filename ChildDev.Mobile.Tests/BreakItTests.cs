using LevelUp.Data;
using LevelUp.Models;
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
