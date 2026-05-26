using LevelUp.Data;
using LevelUp.Models;
using LevelUp.Services;
using SQLite;
using System.Net;
using System.Net.Http.Json;

namespace LevelUp.Tests;

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

public class FakeNavigationService : INavigationService
{
    public List<string> NavigatedRoutes { get; } = [];
    public List<string> AlertTitles { get; } = [];
    public List<string> PromptTitles { get; } = [];

    public bool AlertConfirmResult { get; set; } = true;
    public string? PromptResult { get; set; } = "Test note";
    public List<string> ActionSheetTitles { get; } = [];
    public string? ActionSheetResult { get; set; }

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

    public Task<string?> DisplayActionSheetAsync(string title, string cancel, string? destruction, params string[] buttons)
    {
        ActionSheetTitles.Add(title);
        return Task.FromResult(ActionSheetResult);
    }
}

public class NoOpHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { Records = Array.Empty<object>() })
        });
}
