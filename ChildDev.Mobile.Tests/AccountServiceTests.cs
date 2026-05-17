using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using SQLite;

namespace ChildDev.Mobile.Tests;

public class AccountServiceTests : IDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private readonly AccountService _service;

    public AccountServiceTests()
    {
        SqliteFixture.EnsureInit();
        _db = new SQLiteAsyncConnection(":memory:");
        _db.CreateTableAsync<Account>().GetAwaiter().GetResult();
        _service = new AccountService(_db);
    }

    public void Dispose() => _db.CloseAsync().GetAwaiter().GetResult();

    [Fact]
    public async Task GetAccount_WhenNone_ReturnsNull()
    {
        var account = await _service.GetAccountAsync();
        Assert.Null(account);
    }

    [Fact]
    public async Task CreateAccount_StoresPinHash_NotRawPin()
    {
        await _service.CreateAccountAsync("alice", "1234");
        var account = await _service.GetAccountAsync();

        Assert.NotNull(account);
        Assert.Equal("alice", account.NickName);
        Assert.NotEqual("1234", account.PinHash);
    }

    [Fact]
    public async Task VerifyPin_CorrectPin_ReturnsTrue()
    {
        await _service.CreateAccountAsync("bob", "9876");
        var result = await _service.VerifyPinAsync("9876");
        Assert.True(result);
    }

    [Fact]
    public async Task VerifyPin_WrongPin_ReturnsFalse()
    {
        await _service.CreateAccountAsync("carol", "1111");
        var result = await _service.VerifyPinAsync("9999");
        Assert.False(result);
    }

    [Fact]
    public async Task UpdateLastSync_SetsTimestamp()
    {
        await _service.CreateAccountAsync("dave", "0000");
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _service.UpdateLastSyncAsync(ts);

        var account = await _service.GetAccountAsync();
        Assert.Equal(ts, account!.LastSyncAt);
    }

    [Fact]
    public async Task SaveServerCredentials_PersistsJwtAndUrl()
    {
        await _service.CreateAccountAsync("eve", "1234");
        await _service.SaveServerCredentialsAsync("test-jwt-token", "https://example.com");

        var account = await _service.GetAccountAsync();
        Assert.Equal("test-jwt-token", account!.ServerJwt);
        Assert.Equal("https://example.com", account.ServerUrl);
    }

    [Fact]
    public async Task SaveServerUrl_UpdatesUrlWithoutAffectingJwt()
    {
        await _service.CreateAccountAsync("frank", "1234");
        await _service.SaveServerCredentialsAsync("existing-jwt", "https://old.example.com");
        await _service.SaveServerUrlAsync("https://new.example.com");

        var account = await _service.GetAccountAsync();
        Assert.Equal("https://new.example.com", account!.ServerUrl);
        Assert.Equal("existing-jwt", account.ServerJwt);
    }
}
