using LevelUp.Data;
using LevelUp.Models;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

/// <summary>
/// Full CRUD round-trip tests: add, modify (load→edit→save), and delete for every entity.
/// Covers GoalEntry delete/complete, JournalEntry edit+delete, GoalList QuickNote save,
/// RemindersViewModel dismiss/snooze/add-general, and TodoEntry linked-goal edit.
/// </summary>

// ─── GOAL ENTRY ─────────────────────────────────────────────────────────────

public class GoalEntryDeleteTests : ViewModelTestBase
{
    private GoalEntryViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task Delete_Confirmed_RemovesGoalAndProgressNotes()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn drums", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), GoalFk = goal.Guid, AccountFk = account.Guid, NextStepItems = "Practice rudiments", UpdatedOn = ts });

        Nav.AlertConfirmResult = true;
        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        await vm.DeleteCommand.ExecuteAsync(null);

        // Soft delete: record still exists but DeletedAt is set
        var deleted = await GoalRepo.GetAsync(goal.Guid);
        Assert.NotNull(deleted!.DeletedAt);
        var notes = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Empty(notes);
    }

    [Fact]
    public async Task Delete_Cancelled_GoalRemainsInDatabase()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Survive", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        Nav.AlertConfirmResult = false;
        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.NotNull(await GoalRepo.GetAsync(goal.Guid));
    }

    [Fact]
    public async Task Delete_NoGuid_DoesNothing()
    {
        var vm = BuildVm();
        await vm.DeleteCommand.ExecuteAsync(null); // should not throw
    }

    [Fact]
    public async Task MarkComplete_WithGuid_SetsCompletionDate()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Run 5k", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.False(vm.IsCompleted);
        await vm.MarkCompleteCommand.ExecuteAsync(null);

        var updated = await GoalRepo.GetAsync(goal.Guid);
        Assert.NotNull(updated!.CompletionDate);
    }

    [Fact]
    public async Task MarkComplete_NoGuid_DoesNothing()
    {
        var vm = BuildVm();
        await vm.MarkCompleteCommand.ExecuteAsync(null); // no exception
    }

    [Fact]
    public async Task AddLinkedTodo_WithPromptResult_CreatesTodo()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn piano", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = "Practice C major scale";
        var vm = BuildVm();
        vm.Guid = goal.Guid;
        vm.GoalText = "Learn piano";
        await Task.Delay(200);

        await vm.AddLinkedTodoCommand.ExecuteAsync(null);

        Assert.Single(vm.LinkedTodos);
        Assert.Equal("Practice C major scale", vm.LinkedTodos[0].Title);
        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(todos);
    }

    [Fact]
    public async Task AddLinkedTodo_Cancelled_NoTodoCreated()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Meditate", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = null;
        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);

        await vm.AddLinkedTodoCommand.ExecuteAsync(null);

        Assert.Empty(vm.LinkedTodos);
    }

    [Fact]
    public async Task AddLinkedTodo_NoGuid_DoesNothing()
    {
        var vm = BuildVm();
        Nav.PromptResult = "Should not be saved";
        await vm.AddLinkedTodoCommand.ExecuteAsync(null); // no exception
    }

    [Fact]
    public async Task Save_ExistingGoal_EditGoalText_PersistsChange()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Original text", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);
        vm.GoalText = "Updated text";
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = await GoalRepo.GetAsync(goal.Guid);
        Assert.Equal("Updated text", saved!.GoalText);
    }

    [Fact]
    public async Task Save_ExistingGoal_EditMeasurableOutcome_PersistsChange()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Get fit", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        var vm = BuildVm();
        vm.Guid = goal.Guid;
        await Task.Delay(200);
        vm.MeasurableOutcome = "Run 5k under 30 minutes";
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = await GoalRepo.GetAsync(goal.Guid);
        Assert.Equal("Run 5k under 30 minutes", saved!.MeasurableOutcome);
    }
}

// ─── JOURNAL ENTRY ──────────────────────────────────────────────────────────

public class JournalEntryEditTests : ViewModelTestBase
{
    private JournalEntryViewModel BuildVm() =>
        new(JournalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task Save_ExistingJournal_EditNotes_PersistsChange()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Original notes", EnteredDate = ts };
        await JournalRepo.SaveAsync(journal);

        var vm = BuildVm();
        vm.Guid = journal.Guid;
        await Task.Delay(200);

        Assert.Equal("Original notes", vm.Notes);
        vm.Notes = "Updated notes";
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = await JournalRepo.GetAsync(journal.Guid);
        Assert.Equal("Updated notes", saved!.Notes);
    }

    [Fact]
    public async Task Save_ExistingJournal_EditMood_PersistsChange()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Day log", Mood = "😐 Neutral", EnteredDate = ts };
        await JournalRepo.SaveAsync(journal);

        var vm = BuildVm();
        vm.Guid = journal.Guid;
        await Task.Delay(200);

        Assert.Equal("😐 Neutral", vm.Mood);
        vm.Mood = "😊 Happy";
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = await JournalRepo.GetAsync(journal.Guid);
        Assert.Equal("😊 Happy", saved!.Mood);
    }

    [Fact]
    public async Task Save_ExistingJournal_EditActivity_PersistsChange()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Activity = "Swimming", EnteredDate = ts };
        await JournalRepo.SaveAsync(journal);

        var vm = BuildVm();
        vm.Guid = journal.Guid;
        await Task.Delay(200);

        vm.Activity = "Running";
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = await JournalRepo.GetAsync(journal.Guid);
        Assert.Equal("Running", saved!.Activity);
    }

    [Fact]
    public async Task Save_ExistingJournal_EditTags_PersistsChange()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Entry", Tags = "school", EnteredDate = ts };
        await JournalRepo.SaveAsync(journal);

        var vm = BuildVm();
        vm.Guid = journal.Guid;
        await Task.Delay(200);

        vm.Tags = "school, sports";
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = await JournalRepo.GetAsync(journal.Guid);
        Assert.Equal("school, sports", saved!.Tags);
    }

    [Fact]
    public async Task Save_ExistingJournal_EditEmotionReason_PersistsChange()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Felt off today", EmotionReason = "Tired", EnteredDate = ts };
        await JournalRepo.SaveAsync(journal);

        var vm = BuildVm();
        vm.Guid = journal.Guid;
        await Task.Delay(200);

        vm.EmotionReason = "Big test coming up";
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = await JournalRepo.GetAsync(journal.Guid);
        Assert.Equal("Big test coming up", saved!.EmotionReason);
    }

    [Fact]
    public async Task Delete_Confirmed_RemovesJournal()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Delete me", EnteredDate = ts };
        await JournalRepo.SaveAsync(journal);

        Nav.AlertConfirmResult = true;
        var vm = BuildVm();
        vm.Guid = journal.Guid;
        await Task.Delay(200);

        await vm.DeleteCommand.ExecuteAsync(null);

        var after = await JournalRepo.GetAsync(journal.Guid);
        Assert.True(after is null || after.DeletedAt.HasValue);
    }

    [Fact]
    public async Task Delete_Cancelled_JournalRemains()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Keep me", EnteredDate = ts };
        await JournalRepo.SaveAsync(journal);

        Nav.AlertConfirmResult = false;
        var vm = BuildVm();
        vm.Guid = journal.Guid;
        await Task.Delay(200);

        await vm.DeleteCommand.ExecuteAsync(null);

        var after = await JournalRepo.GetAsync(journal.Guid);
        Assert.NotNull(after);
        Assert.Null(after!.DeletedAt);
    }

    [Fact]
    public async Task Delete_NoGuid_DoesNothing()
    {
        var vm = BuildVm();
        await vm.DeleteCommand.ExecuteAsync(null); // no exception
    }
}

// ─── GOAL LIST — QUICK NOTE ──────────────────────────────────────────────────

public class GoalListQuickNoteSaveTests : ViewModelTestBase
{
    private GoalListViewModel BuildVm() =>
        new(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task QuickNote_WithNote_SavesProgressNote()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Learn guitar", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        Nav.PromptResult = "Practiced for 30 minutes";
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.QuickNoteCommand.ExecuteAsync(vm.Goals[0]);

        var notes = await GoalProgressRepo.GetForGoalAsync(goal.Guid);
        Assert.Single(notes);
        Assert.Equal("Practiced for 30 minutes", notes[0].NextStepItems);
    }

}

// ─── REMINDERS VIEWMODEL ────────────────────────────────────────────────────

public class RemindersViewModelCrudTests : ViewModelTestBase
{
    private RemindersViewModel BuildVm() =>
        new(ReminderSvc, AccountService, Nav);

    [Fact]
    public async Task Dismiss_RemovesReminderFromList()
    {
        var account = await CreateTestAccountAsync();
        var fireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        var reminder = new Reminder { AccountFk = account.Guid, Title = "Check progress", Topic = "Goal", FireAt = fireAt };
        await ReminderSvc.ScheduleAsync(reminder);

        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Reminders);

        await vm.DismissCommand.ExecuteAsync(vm.Reminders[0]);

        Assert.Empty(vm.Reminders);
        Assert.False(vm.HasReminders);
        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task Snooze_UpdatesFireAtAndReloads()
    {
        var account = await CreateTestAccountAsync();
        var fireAt = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds();
        var reminder = new Reminder { AccountFk = account.Guid, Title = "Quick reminder", Topic = "General", FireAt = fireAt };
        await ReminderSvc.ScheduleAsync(reminder);

        Nav.ActionSheetResult = "1 hour";
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Reminders);
        var originalFireAt = vm.Reminders[0].FireAt;

        await vm.SnoozeCommand.ExecuteAsync(vm.Reminders[0]);

        // After snooze the reminder should still be pending but with updated FireAt
        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.True(pending[0].FireAt > originalFireAt);
    }

    [Fact]
    public async Task Snooze_Cancelled_DoesNotChangeFireAt()
    {
        var account = await CreateTestAccountAsync();
        var fireAt = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds();
        var reminder = new Reminder { AccountFk = account.Guid, Title = "Study", Topic = "General", FireAt = fireAt };
        await ReminderSvc.ScheduleAsync(reminder);

        Nav.ActionSheetResult = null; // user cancels snooze picker
        var vm = BuildVm();
        await vm.LoadCommand.ExecuteAsync(null);
        var originalFireAt = vm.Reminders[0].FireAt;

        await vm.SnoozeCommand.ExecuteAsync(vm.Reminders[0]);

        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Equal(originalFireAt, pending[0].FireAt);
    }

    [Fact]
    public async Task AddGeneral_WithTitleAndDuration_SavesAndClearsTitle()
    {
        var account = await CreateTestAccountAsync();
        Nav.ActionSheetResult = "1 hour";

        var vm = BuildVm();
        vm.NewReminderTitle = "Review homework";
        await vm.AddGeneralCommand.ExecuteAsync(null);

        Assert.Empty(vm.NewReminderTitle);
        var pending = await ReminderSvc.GetPendingAsync(account.Guid);
        Assert.Single(pending);
        Assert.Equal("Review homework", pending[0].Title);
        Assert.Equal("General", pending[0].Topic);
    }

    [Fact]
    public async Task AddGeneral_CancelledSnooze_DoesNotSave()
    {
        await CreateTestAccountAsync();
        Nav.ActionSheetResult = null; // cancel snooze picker

        var vm = BuildVm();
        vm.NewReminderTitle = "Cancelled reminder";
        await vm.AddGeneralCommand.ExecuteAsync(null);

        Assert.Equal("Cancelled reminder", vm.NewReminderTitle); // title NOT cleared
    }

    [Fact]
    public void CanAddGeneral_EmptyTitle_ReturnsFalse()
    {
        var vm = BuildVm();
        vm.NewReminderTitle = string.Empty;
        Assert.False(vm.AddGeneralCommand.CanExecute(null));
    }

    [Fact]
    public void CanAddGeneral_WithTitle_ReturnsTrue()
    {
        var vm = BuildVm();
        vm.NewReminderTitle = "Some title";
        Assert.True(vm.AddGeneralCommand.CanExecute(null));
    }
}

// ─── TODO ENTRY — LINKED GOAL EDIT ──────────────────────────────────────────

public class TodoEntryLinkedGoalEditTests : ViewModelTestBase
{
    private TodoEntryViewModel BuildVm() =>
        new(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public async Task Save_ExistingTodo_ChangingLinkedGoal_UpdatesNotes()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal1 = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "First goal", EnteredDate = ts };
        var goal2 = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Second goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal1);
        await GoalRepo.SaveAsync(goal2);

        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Do something", Notes = "Goal: First goal", UpdatedOn = ts };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        Assert.NotNull(vm.LinkedGoal);
        Assert.Equal("First goal", vm.LinkedGoal!.GoalText);

        vm.LinkedGoal = goal2;
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = await TodoRepo.GetAsync(todo.Guid);
        Assert.StartsWith("Goal: Second goal", saved!.Notes);
        Assert.DoesNotContain("First goal", saved.Notes);
    }

    [Fact]
    public async Task Save_ExistingTodo_RemoveDueDate_NullsItInDb()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var dueMs = DateTimeOffset.UtcNow.AddDays(3).ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Had a due date", UpdatedOn = ts, DueDate = dueMs };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        Assert.True(vm.HasDueDate);
        vm.HasDueDate = false;
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = await TodoRepo.GetAsync(todo.Guid);
        Assert.Null(saved!.DueDate);
    }

    [Fact]
    public async Task Save_ExistingTodo_UpdateTitle_PersistsChange()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Old title", UpdatedOn = ts };
        await TodoRepo.SaveAsync(todo);

        var vm = BuildVm();
        vm.Guid = todo.Guid;
        await Task.Delay(200);

        vm.Title = "New title";
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = await TodoRepo.GetAsync(todo.Guid);
        Assert.Equal("New title", saved!.Title);
    }
}
