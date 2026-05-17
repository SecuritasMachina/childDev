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

    [Fact]
    public async Task VerifyPin_WhenNoAccount_ReturnsFalse()
    {
        var result = await _service.VerifyPinAsync("any-pin");
        Assert.False(result);
    }

    [Fact]
    public async Task UpdateLastSync_WhenNoAccount_DoesNotThrow()
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _service.UpdateLastSyncAsync(ts);
        var account = await _service.GetAccountAsync();
        Assert.Null(account);
    }

    [Fact]
    public async Task CreateAccount_SetsCreatedOn()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _service.CreateAccountAsync("george", "1234");
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var account = await _service.GetAccountAsync();
        Assert.NotNull(account);
        Assert.True(account!.CreatedOn >= before);
        Assert.True(account.CreatedOn <= after);
    }

    [Fact]
    public async Task SaveServerCredentials_WhenNoAccount_DoesNotThrow()
    {
        await _service.SaveServerCredentialsAsync("some-jwt", "https://server.example.com");
        var account = await _service.GetAccountAsync();
        Assert.Null(account);
    }

    [Fact]
    public async Task SaveServerUrl_WhenNoAccount_DoesNotThrow()
    {
        await _service.SaveServerUrlAsync("https://server.example.com");
        var account = await _service.GetAccountAsync();
        Assert.Null(account);
    }

    [Fact]
    public async Task CreateAccount_AssignsNonEmptyGuid()
    {
        await _service.CreateAccountAsync("heidi", "1234");
        var account = await _service.GetAccountAsync();

        Assert.NotNull(account);
        Assert.False(string.IsNullOrEmpty(account!.Guid));
        Assert.True(System.Guid.TryParse(account.Guid, out _));
    }

    [Fact]
    public async Task SaveServerCredentials_WhenCalledTwice_SecondCredentialsPersisted()
    {
        await _service.CreateAccountAsync("jill", "1234");
        await _service.SaveServerCredentialsAsync("jwt-v1", "https://server1.example.com");
        await _service.SaveServerCredentialsAsync("jwt-v2", "https://server2.example.com");

        var account = await _service.GetAccountAsync();
        Assert.Equal("jwt-v2", account!.ServerJwt);
        Assert.Equal("https://server2.example.com", account.ServerUrl);
    }

    [Fact]
    public async Task UpdateLastSync_WhenCalledTwice_SecondTimestampPersisted()
    {
        await _service.CreateAccountAsync("ivan", "1234");
        var t1 = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        var t2 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _service.UpdateLastSyncAsync(t1);
        await _service.UpdateLastSyncAsync(t2);

        var account = await _service.GetAccountAsync();
        Assert.Equal(t2, account!.LastSyncAt);
    }
}
