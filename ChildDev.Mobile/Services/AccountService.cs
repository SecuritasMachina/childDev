using BCrypt.Net;
using ChildDev.Mobile.Models;
using SQLite;

namespace ChildDev.Mobile.Services;

public class AccountService(SQLiteAsyncConnection db)
{
    public async Task<Account?> GetAccountAsync() =>
        await db.Table<Account>().FirstOrDefaultAsync();

    public async Task<Account> CreateAccountAsync(string nickName, string pin)
    {
        var account = new Account
        {
            Guid = Guid.NewGuid().ToString(),
            NickName = nickName,
            PinHash = BCrypt.Net.BCrypt.HashPassword(pin),
            CreatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await db.InsertAsync(account);
        return account;
    }

    public async Task<bool> VerifyPinAsync(string pin)
    {
        var account = await GetAccountAsync();
        return account is not null && BCrypt.Net.BCrypt.Verify(pin, account.PinHash);
    }

    public async Task UpdateLastSyncAsync(long timestamp)
    {
        var account = await GetAccountAsync();
        if (account is null) return;
        account.LastSyncAt = timestamp;
        await db.UpdateAsync(account);
    }

    public async Task SaveServerCredentialsAsync(string jwt, string serverUrl)
    {
        var account = await GetAccountAsync();
        if (account is null) return;
        account.ServerJwt = jwt;
        account.ServerUrl = serverUrl;
        await db.UpdateAsync(account);
    }

    public async Task SaveServerUrlAsync(string serverUrl)
    {
        var account = await GetAccountAsync();
        if (account is null) return;
        account.ServerUrl = serverUrl;
        await db.UpdateAsync(account);
    }
}
