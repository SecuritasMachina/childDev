using BCrypt.Net;
using LevelUp.Models;
using SQLite;

namespace LevelUp.Services;

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
        await db.ExecuteAsync("UPDATE Account SET LastSyncAt = ?", timestamp);
    }

    public Task SaveServerCredentialsAsync(string jwt, string serverUrl) =>
        db.ExecuteAsync("UPDATE Account SET ServerJwt = ?, ServerUrl = ?", jwt, serverUrl);

    public Task SaveServerUrlAsync(string serverUrl) =>
        db.ExecuteAsync("UPDATE Account SET ServerUrl = ?", serverUrl);

    public Task ClearServerJwtAsync() =>
        db.ExecuteAsync("UPDATE Account SET ServerJwt = NULL");

    // Links the mobile account to a server account by migrating the local GUID to the server's
    // GUID so that all synced records carry the correct AccountFk.
    public async Task LinkToServerAsync(string jwt, string serverUrl, string serverAccountGuid)
    {
        var account = await GetAccountAsync();
        if (account is null) return;
        var oldGuid = account.Guid;
        if (oldGuid != serverAccountGuid)
        {
            await db.ExecuteAsync("UPDATE Journal SET AccountFk = ? WHERE AccountFk = ?", serverAccountGuid, oldGuid);
            await db.ExecuteAsync("UPDATE Goal SET AccountFk = ? WHERE AccountFk = ?", serverAccountGuid, oldGuid);
            await db.ExecuteAsync("UPDATE GoalProgress SET AccountFk = ? WHERE AccountFk = ?", serverAccountGuid, oldGuid);
            await db.ExecuteAsync("UPDATE Todo SET AccountFk = ? WHERE AccountFk = ?", serverAccountGuid, oldGuid);
            await db.ExecuteAsync("UPDATE Reminder SET AccountFk = ? WHERE AccountFk = ?", serverAccountGuid, oldGuid);
            await db.ExecuteAsync(
                "UPDATE Account SET Guid = ?, ServerJwt = ?, ServerUrl = ? WHERE Guid = ?",
                serverAccountGuid, jwt, serverUrl, oldGuid);
        }
        else
        {
            await db.ExecuteAsync(
                "UPDATE Account SET ServerJwt = ?, ServerUrl = ? WHERE Guid = ?",
                jwt, serverUrl, oldGuid);
        }
    }
}
