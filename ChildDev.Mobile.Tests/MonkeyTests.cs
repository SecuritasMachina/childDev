using LevelUp.Data;
using LevelUp.Models;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

/// <summary>
/// Monkey/fuzz tests: random sequences of property mutations and command invocations
/// on each ViewModel. Goal is to find null-refs, unhandled exceptions, and state
/// corruption under inputs no developer thought to test explicitly.
/// No assertions on specific values — just "should not throw".
/// </summary>
public class MonkeyTests : ViewModelTestBase
{
    private static readonly Random Rng = new(42); // fixed seed for reproducibility

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string? RandomString() => Rng.Next(4) switch
    {
        0 => null,
        1 => string.Empty,
        2 => new string('x', Rng.Next(1, 500)),
        _ => $"random_{Rng.Next(10000)}"
    };

    private static bool RandomBool() => Rng.Next(2) == 0;

    private static DateTime RandomDate() =>
        DateTime.Today.AddDays(Rng.Next(-365, 365));

    // ── GoalEntryViewModel ───────────────────────────────────────────────────

    [Fact]
    public async Task GoalEntry_RandomPropertyAndCommandSequence_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Pre-populate some goals and progress notes to maximise code paths
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Fuzz goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        for (int i = 0; i < 3; i++)
            await GoalProgressRepo.SaveAsync(new GoalProgress { Guid = Guid.NewGuid().ToString(), GoalFk = goal.Guid, AccountFk = account.Guid, NextStepItems = $"Note {i}", UpdatedOn = ts + i });

        var vm = new GoalEntryViewModel(GoalRepo, GoalProgressRepo, TodoRepo, AccountService, Analytics, Nav, ReminderSvc);

        Nav.AlertConfirmResult = RandomBool();
        Nav.PromptResult = RandomString();

        // Random property mutations
        for (int i = 0; i < 20; i++)
        {
            vm.GoalText = RandomString() ?? string.Empty;
            vm.NextStepItems = RandomString() ?? string.Empty;
            vm.MeasurableOutcome = RandomString() ?? string.Empty;
            vm.ProgressPercent = Rng.Next(-10, 110);
            vm.Guid = Rng.Next(3) == 0 ? goal.Guid : (RandomString() ?? string.Empty);
            await Task.Delay(10);
        }

        // Random command invocations — none should throw
        await vm.SaveCommand.ExecuteAsync(null);
        await vm.ReopenCommand.ExecuteAsync(null);
        vm.SetNoteTemplateCommand.Execute("A win today: ");
        await vm.ShareProgressCommand.ExecuteAsync(null);
        await vm.AddLinkedTodoCommand.ExecuteAsync(null);
    }

    // ── GoalListViewModel ────────────────────────────────────────────────────

    [Fact]
    public async Task GoalList_RandomFilterAndCommandSequence_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < 5; i++)
            await GoalRepo.SaveAsync(new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = $"Goal {i}", EnteredDate = ts + i });

        var vm = new GoalListViewModel(GoalRepo, GoalProgressRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        Nav.AlertConfirmResult = RandomBool();
        Nav.PromptResult = RandomString();

        await vm.LoadCommand.ExecuteAsync(null);

        string[] categories = ["All", "NeedsAttention", "School", "Sports", "Health", string.Empty, null!];
        string?[] filterTexts = [null, string.Empty, "goal", "xyz", "a", new string('z', 100)];

        for (int i = 0; i < 15; i++)
        {
            vm.FilterText = filterTexts[Rng.Next(filterTexts.Length)] ?? string.Empty;
            vm.SetCategoryFilterCommand.Execute(categories[Rng.Next(categories.Length)]);
        }

        if (vm.Goals.Count > 0)
        {
            await vm.QuickNoteCommand.ExecuteAsync(vm.Goals[0]);
            await vm.TogglePinCommand.ExecuteAsync(vm.Goals[0]);
            await vm.DeleteCommand.ExecuteAsync(vm.Goals[0]);
        }

        await vm.RefreshCommand.ExecuteAsync(null);
    }

    // ── JournalEntryViewModel ────────────────────────────────────────────────

    [Fact]
    public async Task JournalEntry_RandomPropertyAndCommandSequence_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Seed entry", EnteredDate = ts };
        await JournalRepo.SaveAsync(journal);

        var vm = new JournalEntryViewModel(JournalRepo, AccountService, Analytics, Nav, ReminderSvc);
        Nav.AlertConfirmResult = RandomBool();

        string?[] moods = [null, string.Empty, "😊 Happy", "😢 Sad", "😐 Neutral", new string('!', 200)];
        string[] tags = ["school", "sports", "family", "health"];

        for (int i = 0; i < 20; i++)
        {
            vm.Notes = RandomString() ?? string.Empty;
            vm.Activity = RandomString() ?? string.Empty;
            vm.Mood = moods[Rng.Next(moods.Length)] ?? string.Empty;
            vm.EmotionReason = RandomString() ?? string.Empty;
            vm.EnteredDate = RandomDate();
            vm.SetMoodCommand.Execute(moods[Rng.Next(moods.Length)]);
            vm.ToggleTagCommand.Execute(tags[Rng.Next(tags.Length)]);
        }

        vm.Guid = journal.Guid;
        await Task.Delay(100);

        if (vm.SaveCommand.CanExecute(null))
            await vm.SaveCommand.ExecuteAsync(null);
    }

    // ── JournalListViewModel ─────────────────────────────────────────────────

    [Fact]
    public async Task JournalList_RandomFilterAndCommands_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < 5; i++)
            await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = $"Entry {i}", EnteredDate = now - i * 86400000L });

        var vm = new JournalListViewModel(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        Nav.AlertConfirmResult = RandomBool();

        await vm.LoadCommand.ExecuteAsync(null);

        string?[] filters = [null, string.Empty, "entry", "xyz", "0", new string('e', 50)];
        string[] dateFilters = ["All", "Week", "Month", "invalid", string.Empty];

        for (int i = 0; i < 15; i++)
        {
            vm.FilterText = filters[Rng.Next(filters.Length)] ?? string.Empty;
            vm.SetDateFilterCommand.Execute(dateFilters[Rng.Next(dateFilters.Length)]);
        }

        if (vm.Journals.Count > 0)
            await vm.DeleteCommand.ExecuteAsync(vm.Journals[0]);

        await vm.RefreshCommand.ExecuteAsync(null);
        vm.ShufflePromptCommand.Execute(null);
    }

    // ── TodoListViewModel ────────────────────────────────────────────────────

    [Fact]
    public async Task TodoList_RandomTitleAndCommands_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        for (int i = 0; i < 4; i++)
            await TodoRepo.SaveAsync(new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = $"Todo {i}", UpdatedOn = now, DueDate = i % 2 == 0 ? yesterday : null });

        var vm = new TodoListViewModel(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);
        Nav.AlertConfirmResult = RandomBool();

        await vm.LoadCommand.ExecuteAsync(null);

        string?[] newTitles = [null, string.Empty, "  ", "New task", new string('t', 300)];
        string?[] filters = [null, string.Empty, "todo", "xyz"];

        for (int i = 0; i < 15; i++)
        {
            vm.NewTodoTitle = newTitles[Rng.Next(newTitles.Length)] ?? string.Empty;
            vm.FilterText = filters[Rng.Next(filters.Length)] ?? string.Empty;
        }

        vm.NewTodoTitle = "Fuzz todo";
        await vm.AddCommand.ExecuteAsync(null);

        if (vm.Todos.Count > 0)
            await vm.CompleteCommand.ExecuteAsync(vm.Todos[0]);

        await vm.SnoozeOverdueCommand.ExecuteAsync(null);
        await vm.RefreshCommand.ExecuteAsync(null);
    }

    // ── TodoEntryViewModel ───────────────────────────────────────────────────

    [Fact]
    public async Task TodoEntry_RandomPropertyAndCommandSequence_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var goal = new Goal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, GoalText = "Fuzz linked goal", EnteredDate = ts };
        await GoalRepo.SaveAsync(goal);
        var todo = new Todo { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Title = "Fuzz todo", UpdatedOn = ts };
        await TodoRepo.SaveAsync(todo);

        var vm = new TodoEntryViewModel(TodoRepo, GoalRepo, AccountService, Analytics, Nav, ReminderSvc);
        Nav.AlertConfirmResult = RandomBool();

        for (int i = 0; i < 15; i++)
        {
            vm.Title = RandomString() ?? string.Empty;
            vm.Notes = RandomString() ?? string.Empty;
            vm.HasDueDate = RandomBool();
            vm.DueDate = RandomDate();
            vm.LinkedGoal = Rng.Next(2) == 0 ? goal : null;
        }

        vm.Guid = todo.Guid;
        await Task.Delay(200);

        vm.Title = "Valid title after fuzz";
        await vm.SaveCommand.ExecuteAsync(null);
    }

    // ── RemindersViewModel ───────────────────────────────────────────────────

    [Fact]
    public async Task Reminders_RandomTitleAndCommands_DoesNotThrow()
    {
        var account = await CreateTestAccountAsync();
        var fireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        for (int i = 0; i < 3; i++)
            await ReminderSvc.ScheduleAsync(new Reminder { AccountFk = account.Guid, Title = $"Reminder {i}", Topic = "General", FireAt = fireAt + i });

        var vm = new RemindersViewModel(ReminderSvc, AccountService, Nav);
        Nav.ActionSheetResult = Rng.Next(3) switch { 0 => "1 hour", 1 => null, _ => "1 day" };

        await vm.LoadCommand.ExecuteAsync(null);

        string?[] titles = [null, string.Empty, "  ", "Fuzz reminder", new string('r', 200)];
        for (int i = 0; i < 10; i++)
            vm.NewReminderTitle = titles[Rng.Next(titles.Length)] ?? string.Empty;

        vm.NewReminderTitle = "Valid fuzz reminder";
        if (vm.AddGeneralCommand.CanExecute(null))
            await vm.AddGeneralCommand.ExecuteAsync(null);

        await vm.LoadCommand.ExecuteAsync(null);

        if (vm.Reminders.Count > 0)
        {
            await vm.DismissCommand.ExecuteAsync(vm.Reminders[0]);
            await vm.LoadCommand.ExecuteAsync(null);
        }

        if (vm.Reminders.Count > 0)
            await vm.SnoozeCommand.ExecuteAsync(vm.Reminders[0]);
    }

    // ── SettingsViewModel ────────────────────────────────────────────────────

    [Fact]
    public async Task Settings_RandomServerUrlAndCommands_DoesNotThrow()
    {
        await CreateTestAccountAsync();

        var factory = new FakeHttpClientFactory(new NoOpHttpHandler());
        var vm = new SettingsViewModel(AccountService, factory, Analytics);

        await vm.LoadCommand.ExecuteAsync(null);

        string?[] urls = [null, string.Empty, "  ", "https://example.com", "not-a-url", new string('x', 500), "http://localhost:5000"];

        for (int i = 0; i < 10; i++)
        {
            vm.ServerUrl = urls[Rng.Next(urls.Length)] ?? string.Empty;
            vm.ServerNickName = RandomString() ?? string.Empty;
            vm.ServerPin = RandomString() ?? string.Empty;
        }

        // TestConnectionCommand and SaveServerUrlCommand hit HTTP — they'll fire but get no-op responses
        await vm.TestConnectionCommand.ExecuteAsync(null);
        await vm.SaveServerUrlCommand.ExecuteAsync(null);
    }
}
