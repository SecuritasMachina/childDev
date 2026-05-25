using System.Net.Http.Headers;
using System.Net.Http.Json;
using LevelUp.Models;

namespace LevelUp.Services;

public class MobileAnalyticsService(AccountService accountService, IHttpClientFactory httpFactory)
{
    private record EventPayload(string EventName, string? Context);

    public void Track(string eventName, string? context = null) =>
        Task.Run(() => SendAsync(eventName, context));

    private async Task SendAsync(string eventName, string? context)
    {
        try
        {
            var account = await accountService.GetAccountAsync();
            if (account is null || string.IsNullOrEmpty(account.ServerUrl) || string.IsNullOrEmpty(account.ServerJwt))
                return;

            var client = httpFactory.CreateClient("childdev");
            client.BaseAddress = new Uri(account.ServerUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", account.ServerJwt);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.PostAsJsonAsync("api/mobile/events",
                new[] { new EventPayload(eventName, context) }, cts.Token);
        }
        catch
        {
            // fire-and-forget — never crash the UI
        }
    }
}
