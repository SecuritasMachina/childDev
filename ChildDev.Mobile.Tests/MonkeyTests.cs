using LevelUp.Data;
using LevelUp.Models;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

/// <summary>
/// Monkey/fuzz tests: random and adversarial inputs on all ViewModels.
/// No value assertions — just "must not throw".
/// Patterns: null entity params, rapid property churn, concurrent command fires,
/// empty-collection commands, boundary values, interleaved load+mutate.
/// </summary>
public class MonkeyTests : ViewModelTestBase
{
    private static readonly Random Rng = new(42);

    private static string? RandomString() => Rng.Next(5) switch
    {
        0 => null,
        1 => string.Empty,
        2 => "   ",
        3 => new string('x', Rng.Next(1, 500)),
        _ => $"rnd_{Rng.Next(100000)}"
    };

    private static bool RandomBool() => Rng.Next(2) == 0;
    private static DateTime RandomDate() => DateTime.Today.AddDays(Rng.Next(-730, 730));
    private static int RandomInt(int min, int max) => Rng.Next(min, max);

    // ── GoalEntryViewModel ───────────────────────────────────────────────────

    [Fact]
    public async Task GoalEntry_RandomPropertyAndCommandSequence_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Fuzz goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        for (int i = 0; i < 3; i++)
            await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), GoalFk = goal.Guid, AccountFk = account.Guid, NextStepItems = $"Note {i}", UpdatedOn = ts + i });

        var vm = new GoalEntryViewModel(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);
        Nav.AlertConfirmResult = RandomBool();
        Nav.PromptResult = RandomString();

        for (int i = 0; i < 25; i++)
        {
            vm.GoalText = RandomString() ?? string.Empty;
            vm.NextStepItems = RandomString() ?? string.Empty;
            vm.MeasurableOutcome = RandomString() ?? string.Empty;
            vm.ProgressPercent = RandomInt(-10, 110);
            vm.Guid = Rng.Next(3) == 0 ? goal.Guid : (RandomString() ?? string.Empty);
            await Task.Delay(5);
        }

        await vm.SaveCommand.ExecuteAsync(null);
        await vm.ReopenCommand.ExecuteAsync(null);
        vm.SetNoteTemplateCommand.Execute("A win today: ");
        vm.SetNoteTemplateCommand.Execute("A win today: ");  // duplicate — should not double-prefix
        await vm.ShareProgressCommand.ExecuteAsync(null);
        await vm.AddLinkedTodoCommand.ExecuteAsync(null);
        await vm.CompleteLinkedTodoCommand.ExecuteAsync(null);  // null → guarded
    }

    [Fact]
    public async Task GoalEntry_ConcurrentSaves_DoNotCorruptDb()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Concurrent", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);

        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            var vm = new GoalEntryViewModel(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);
            vm.Guid = goal.Guid;
            await Task.Delay(10);
            vm.GoalText = $"Version {i}";
            await vm.SaveCommand.ExecuteAsync(null);
        });
        await Task.WhenAll(tasks);

        var saved = await GoalRepo.GetAsync(goal.Guid);
        Assert.NotNull(saved); // must still exist
    }

    // ── GoalListViewModel ────────────────────────────────────────────────────

    [Fact]
    public async Task GoalList_NullEntityCommands_DoNotThrow()
    {
        await CreateTestAccountAsync();
        var vm = new GoalListViewModel(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await vm.LoadCommand.ExecuteAsync(null);

        // All entity commands invoked with null — null guards must catch
        await vm.OpenCommand.ExecuteAsync(null);
        await vm.TogglePinCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(null);
        await vm.QuickNoteCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task GoalList_RapidFilterChurn_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < 10; i++)
            await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = $"G{i}", EnteredDate = ts });

        var vm = new GoalListViewModel(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await vm.LoadCommand.ExecuteAsync(null);

        string[] cats = ["All", "NeedsAttention", "School", "Sports", "Health", string.Empty];
        string?[] texts = [null, "", "g", "xyz", "G0", new string('a', 200)];

        for (int i = 0; i < 50; i++)
        {
            vm.FilterText = texts[Rng.Next(texts.Length)] ?? string.Empty;
            vm.SetCategoryFilterCommand.Execute(cats[Rng.Next(cats.Length)]);
        }
    }

    [Fact]
    public async Task GoalList_DeleteWhileFiltered_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < 5; i++)
            await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = $"Goal {i}", EnteredDate = ts });

        Nav.AlertConfirmResult = true;
        var vm = new GoalListViewModel(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.FilterText = "Goal";
        while (vm.Goals.Count > 0)
            await vm.DeleteCommand.ExecuteAsync(vm.Goals[0]);
    }

    // ── JournalEntryViewModel ────────────────────────────────────────────────

    [Fact]
    public async Task JournalEntry_RandomPropertyAndCommandSequence_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Seed", EnteredDate = ts };
        await JournalRepo.SaveAsync(journal);

        var vm = new JournalEntryViewModel(JournalRepo, AccountService, Analytics, Nav, ReminderSvc);
        Nav.AlertConfirmResult = RandomBool();

        string?[] moods = [null, "", "😊 Happy", "😢 Sad", "😐 Neutral", new string('m', 150)];
        string[] tags = ["school", "sports", "family", "health", "xyz"];

        for (int i = 0; i < 30; i++)
        {
            vm.Notes = RandomString() ?? string.Empty;
            vm.Activity = RandomString() ?? string.Empty;
            vm.Mood = moods[Rng.Next(moods.Length)] ?? string.Empty;
            vm.EmotionReason = RandomString() ?? string.Empty;
            vm.EnteredDate = RandomDate();
            vm.SetMoodCommand.Execute(moods[Rng.Next(moods.Length)]);
            vm.SetActivityCommand.Execute(RandomString());
            vm.ToggleTagCommand.Execute(tags[Rng.Next(tags.Length)]);
        }

        vm.Guid = journal.Guid;
        await Task.Delay(100);

        vm.Notes = "Valid note after fuzz";
        if (vm.SaveCommand.CanExecute(null))
            await vm.SaveCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task JournalEntry_ExtremelyLongFields_SavesWithoutThrow()
    {
        var account = await CreateTestAccountAsync();
        var vm = new JournalEntryViewModel(JournalRepo, AccountService, Analytics, Nav, ReminderSvc);

        vm.Notes = new string('n', 10000);
        vm.Activity = new string('a', 5000);
        vm.Mood = new string('m', 1000);
        vm.EmotionReason = new string('e', 2000);
        vm.Tags = string.Join(", ", Enumerable.Range(0, 50).Select(i => $"tag{i}"));

        await vm.SaveCommand.ExecuteAsync(null);

        var journals = await JournalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(journals);
    }

    // ── JournalListViewModel ─────────────────────────────────────────────────

    [Fact]
    public async Task JournalList_NullEntityCommands_DoNotThrow()
    {
        await CreateTestAccountAsync();
        var vm = new JournalListViewModel(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.OpenCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task JournalList_DeleteAllEntriesOneByOne_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < 6; i++)
            await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = $"Entry {i}", EnteredDate = ts + i });

        Nav.AlertConfirmResult = true;
        var vm = new JournalListViewModel(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(6, vm.Journals.Count);

        while (vm.Journals.Count > 0)
            await vm.DeleteCommand.ExecuteAsync(vm.Journals[0]);

        Assert.Empty(vm.Journals);
    }

    [Fact]
    public async Task JournalList_SetDateFilter_AllValues_DoNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Entry", EnteredDate = ts });

        var vm = new JournalListViewModel(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await vm.LoadCommand.ExecuteAsync(null);

        foreach (var filter in new[] { "Week", "Month", "All", "invalid", "", null })
            vm.SetDateFilterCommand.Execute(filter);
    }

    // ── TodoListViewModel ────────────────────────────────────────────────────

    [Fact]
    public async Task TodoList_NullEntityCommands_DoNotThrow()
    {
        await CreateTestAccountAsync();
        var vm = new TodoListViewModel(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.CompleteCommand.ExecuteAsync(null);
        await vm.UncompleteCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(null);
        await vm.OpenCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task TodoList_CompleteAllThenUncompleteAll_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < 4; i++)
            await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = $"Todo {i}", UpdatedOn = now });

        var vm = new TodoListViewModel(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(4, vm.Todos.Count);

        while (vm.Todos.Count > 0)
            await vm.CompleteCommand.ExecuteAsync(vm.Todos[0]);

        Assert.Empty(vm.Todos);
        Assert.Equal(4, vm.CompletedTodos.Count);

        while (vm.CompletedTodos.Count > 0)
            await vm.UncompleteCommand.ExecuteAsync(vm.CompletedTodos[0]);

        Assert.Empty(vm.CompletedTodos);
    }

    [Fact]
    public async Task TodoList_RapidAddAndFilter_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var vm = new TodoListViewModel(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await vm.LoadCommand.ExecuteAsync(null);

        string?[] titles = [null, "", "   ", "Task A", "Task B", new string('t', 300)];
        string?[] filters = [null, "", "task", "a", "xyz", "T"];

        for (int i = 0; i < 20; i++)
        {
            vm.NewTodoTitle = titles[Rng.Next(titles.Length)] ?? string.Empty;
            vm.FilterText = filters[Rng.Next(filters.Length)] ?? string.Empty;
            if (vm.AddCommand.CanExecute(null))
                await vm.AddCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task TodoList_DeleteCompletedWhileFiltering_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < 4; i++)
            await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = $"Done {i}", UpdatedOn = now, CompletedAt = now });

        Nav.AlertConfirmResult = true;
        var vm = new TodoListViewModel(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.FilterText = "done";
        while (vm.CompletedTodos.Count > 0)
            await vm.DeleteCommand.ExecuteAsync(vm.CompletedTodos[0]);
    }

    // ── TodoEntryViewModel ───────────────────────────────────────────────────

    [Fact]
    public async Task TodoEntry_BoundaryDates_DoNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var vm = new TodoEntryViewModel(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);
        vm.Title = "Boundary test";

        foreach (var date in new[]
        {
            DateTime.MinValue, DateTime.MaxValue,
            DateTime.Today, DateTime.Today.AddYears(-100),
            DateTime.Today.AddYears(100), new DateTime(1970, 1, 1)
        })
        {
            vm.HasDueDate = true;
            vm.DueDate = date;
        }

        vm.HasDueDate = false;
        await vm.SaveCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task TodoEntry_RapidLinkedGoalChanges_DoNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goals = new List<Goal>();
        for (int i = 0; i < 5; i++)
        {
            var g = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = $"Goal {i}", EnteredDate = ts };
            await GoalRepo.SaveAsync(g);
            goals.Add(g);
        }

        var vm = new TodoEntryViewModel(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);
        vm.Title = "Task with changing goal";

        for (int i = 0; i < 20; i++)
        {
            vm.LinkedGoal = Rng.Next(3) == 0 ? null : goals[Rng.Next(goals.Count)];
            vm.Notes = RandomString() ?? string.Empty;
        }

        vm.Title = "Final title";
        await vm.SaveCommand.ExecuteAsync(null);
    }

    // ── RemindersViewModel ───────────────────────────────────────────────────

    [Fact]
    public async Task Reminders_NullEntityCommands_DoNotThrow()
    {
        await CreateTestAccountAsync();
        var vm = new RemindersViewModel(ReminderSvc, AccountService, Nav);
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.SnoozeCommand.ExecuteAsync(null);
        await vm.DismissCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Reminders_DismissAllOneByOne_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var fireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        for (int i = 0; i < 5; i++)
            await ReminderSvc.ScheduleAsync(new Reminder { AccountFk = account.Guid, Title = $"R{i}", Topic = "General", FireAt = fireAt + i });

        var vm = new RemindersViewModel(ReminderSvc, AccountService, Nav);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(5, vm.Reminders.Count);

        while (vm.Reminders.Count > 0)
            await vm.DismissCommand.ExecuteAsync(vm.Reminders[0]);

        Assert.Empty(vm.Reminders);
        Assert.False(vm.HasReminders);
    }

    [Fact]
    public async Task Reminders_RapidTitleChurn_DoesNotThrow()
    {
        await CreateTestAccountAsync();
        var vm = new RemindersViewModel(ReminderSvc, AccountService, Nav);
        Nav.ActionSheetResult = null;

        string?[] titles = [null, "", "  ", "Reminder", new string('r', 300)];
        for (int i = 0; i < 30; i++)
            vm.NewReminderTitle = titles[Rng.Next(titles.Length)] ?? string.Empty;
    }

    // ── SettingsViewModel ────────────────────────────────────────────────────

    [Fact]
    public async Task Settings_RandomServerUrlAndCommands_DoesNotThrow()
    {
        await CreateTestAccountAsync();
        var factory = new FakeHttpClientFactory(new NoOpHttpHandler());
        var vm = new SettingsViewModel(AccountService, factory, Analytics);
        await vm.LoadCommand.ExecuteAsync(null);

        string?[] urls = [null, "", "  ", "https://example.com", "not-a-url", new string('x', 500), "http://localhost:5000", "ftp://bad"];
        for (int i = 0; i < 15; i++)
        {
            vm.ServerUrl = urls[Rng.Next(urls.Length)] ?? string.Empty;
            vm.ServerNickName = RandomString() ?? string.Empty;
            vm.ServerPin = RandomString() ?? string.Empty;
        }

        await vm.TestConnectionCommand.ExecuteAsync(null);
        await vm.SaveServerUrlCommand.ExecuteAsync(null);
        await vm.UnlinkFromServerCommand.ExecuteAsync(null);
    }

    // ── Cross-ViewModel consistency ──────────────────────────────────────────

    [Fact]
    public async Task CrossViewModel_AddGoalThenAddTodoLinkedToIt_StateConsistent()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Save goal and a linked todo, then fuzz-check that GoalEntry shows the linked todo
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Cross-vm goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        for (int i = 0; i < 3; i++)
            await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = $"Task {i}", Notes = "Goal: Cross-vm goal", UpdatedOn = ts + i });

        var goalVm = new GoalEntryViewModel(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);
        goalVm.Guid = goal.Guid;
        await Task.Delay(200);

        Assert.Equal(3, goalVm.LinkedTodos.Count);

        // Complete them all via the GoalEntry ViewModel
        while (goalVm.LinkedTodos.Count > 0)
            await goalVm.CompleteLinkedTodoCommand.ExecuteAsync(goalVm.LinkedTodos[0]);

        Assert.Empty(goalVm.LinkedTodos);
        Assert.False(goalVm.HasLinkedTodos);

        // Verify in TodoList that they're completed
        var todoVm = new TodoListViewModel(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await todoVm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(3, todoVm.CompletedTodoCount);
    }

    [Fact]
    public async Task CrossViewModel_JournalStreakThenDeleteEntries_StreakResetsCorrectly()
    {
        var account = await CreateTestAccountAsync();
        // 5-day streak, no today entry → warning
        for (int d = 1; d <= 5; d++)
        {
            var entryTs = DateTimeOffset.UtcNow.AddDays(-d).ToUnixTimeMilliseconds();
            await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = $"Day -{d}", EnteredDate = entryTs });
        }

        var listVm = new JournalListViewModel(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        await listVm.LoadCommand.ExecuteAsync(null);
        Assert.True(listVm.HasStreakWarning);

        // Delete all entries via list
        Nav.AlertConfirmResult = true;
        while (listVm.Journals.Count > 0)
            await listVm.DeleteCommand.ExecuteAsync(listVm.Journals[0]);

        // Reload — no entries → no streak warning
        await listVm.LoadCommand.ExecuteAsync(null);
        Assert.False(listVm.HasStreakWarning);
    }
}
