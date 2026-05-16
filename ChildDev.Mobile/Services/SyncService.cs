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
    public Task<SyncResult> RunAsync(Account account) =>
        Task.FromResult(SyncResult.NoServer);
}
