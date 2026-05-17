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

    [Fact]
    public async Task UpdateLastSync_PreservesServerCredentials()
    {
        await _service.CreateAccountAsync("leo", "1234");
        await _service.SaveServerCredentialsAsync("my-jwt", "https://myserver.com");

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _service.UpdateLastSyncAsync(ts);

        var account = await _service.GetAccountAsync();
        Assert.Equal("my-jwt", account!.ServerJwt);
        Assert.Equal("https://myserver.com", account.ServerUrl);
        Assert.Equal(ts, account.LastSyncAt);
    }

    [Fact]
    public async Task SaveServerCredentials_PreservesNickNameAndCreatedOn()
    {
        await _service.CreateAccountAsync("kate", "5678");
        var before = await _service.GetAccountAsync();
        var originalNickName = before!.NickName;
        var originalCreatedOn = before.CreatedOn;

        await _service.SaveServerCredentialsAsync("new-jwt", "https://server.example.com");

        var after = await _service.GetAccountAsync();
        Assert.Equal(originalNickName, after!.NickName);
        Assert.Equal(originalCreatedOn, after.CreatedOn);
        Assert.Equal("new-jwt", after.ServerJwt);
        Assert.Equal("https://server.example.com", after.ServerUrl);
    }

    [Fact]
    public async Task SaveServerCredentials_PreservesLastSyncAt()
    {
        await _service.CreateAccountAsync("sam", "1234");
        await _service.UpdateLastSyncAsync(7_000_000L);

        await _service.SaveServerCredentialsAsync("my-jwt", "https://sync.example.com");

        var after = await _service.GetAccountAsync();
        Assert.Equal(7_000_000L, after!.LastSyncAt);
    }

    [Fact]
    public async Task SaveServerUrl_PreservesLastSyncAt()
    {
        await _service.CreateAccountAsync("taylor", "1234");
        await _service.UpdateLastSyncAsync(8_000_000L);

        await _service.SaveServerUrlAsync("https://new-server.example.com");

        var after = await _service.GetAccountAsync();
        Assert.Equal(8_000_000L, after!.LastSyncAt);
    }
}

public class AccountServiceLinkTests : IDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private readonly AccountService _service;

    public AccountServiceLinkTests()
    {
        SqliteFixture.EnsureInit();
        _db = new SQLiteAsyncConnection(":memory:");
        _db.CreateTableAsync<Account>().GetAwaiter().GetResult();
        _db.CreateTableAsync<Journal>().GetAwaiter().GetResult();
        _db.CreateTableAsync<Goal>().GetAwaiter().GetResult();
        _db.CreateTableAsync<GoalProgress>().GetAwaiter().GetResult();
        _db.CreateTableAsync<Todo>().GetAwaiter().GetResult();
        _service = new AccountService(_db);
    }

    public void Dispose() => _db.CloseAsync().GetAwaiter().GetResult();

    [Fact]
    public async Task LinkToServer_SameGuid_SavesJwtAndUrl()
    {
        await _service.CreateAccountAsync("alice", "1234");
        var original = await _service.GetAccountAsync();

        await _service.LinkToServerAsync("test-jwt", "https://server.example.com", original!.Guid);

        var account = await _service.GetAccountAsync();
        Assert.Equal("test-jwt", account!.ServerJwt);
        Assert.Equal("https://server.example.com", account.ServerUrl);
        Assert.Equal(original.Guid, account.Guid);
    }

    [Fact]
    public async Task LinkToServer_DifferentGuid_MigratesAccountGuid()
    {
        await _service.CreateAccountAsync("bob", "1234");
        var oldAccount = await _service.GetAccountAsync();
        var oldGuid = oldAccount!.Guid;
        var newGuid = System.Guid.NewGuid().ToString();

        await _service.LinkToServerAsync("my-jwt", "https://server.example.com", newGuid);

        var account = await _service.GetAccountAsync();
        Assert.NotNull(account);
        Assert.Equal(newGuid, account!.Guid);
        Assert.Equal("my-jwt", account.ServerJwt);
    }

    [Fact]
    public async Task LinkToServer_DifferentGuid_MigratesJournalAccountFk()
    {
        await _service.CreateAccountAsync("carol", "1234");
        var old = await _service.GetAccountAsync();
        var oldGuid = old!.Guid;
        var newGuid = System.Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertAsync(new Journal { Guid = System.Guid.NewGuid().ToString(), AccountFk = oldGuid, Notes = "test", EnteredDate = ts, UpdatedOn = ts });

        await _service.LinkToServerAsync("jwt", "https://server.com", newGuid);

        var journals = await _db.Table<Journal>().Where(j => j.AccountFk == newGuid).ToListAsync();
        Assert.Single(journals);
        var orphaned = await _db.Table<Journal>().Where(j => j.AccountFk == oldGuid).ToListAsync();
        Assert.Empty(orphaned);
    }

    [Fact]
    public async Task LinkToServer_DifferentGuid_MigratesGoalAccountFk()
    {
        await _service.CreateAccountAsync("dave", "1234");
        var old = await _service.GetAccountAsync();
        var oldGuid = old!.Guid;
        var newGuid = System.Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertAsync(new Goal { Guid = System.Guid.NewGuid().ToString(), AccountFk = oldGuid, EnteredDate = ts, UpdatedOn = ts });

        await _service.LinkToServerAsync("jwt", "https://server.com", newGuid);

        var goals = await _db.Table<Goal>().Where(g => g.AccountFk == newGuid).ToListAsync();
        Assert.Single(goals);
    }

    [Fact]
    public async Task ClearServerJwt_RemovesJwtOnly()
    {
        await _service.CreateAccountAsync("eve", "1234");
        await _service.SaveServerCredentialsAsync("my-jwt", "https://server.com");
        await _service.ClearServerJwtAsync();

        var account = await _service.GetAccountAsync();
        Assert.Null(account!.ServerJwt);
        Assert.Equal("https://server.com", account.ServerUrl);
    }

    [Fact]
    public async Task ClearServerJwt_WhenNoAccount_DoesNotThrow()
    {
        await _service.ClearServerJwtAsync();
        var account = await _service.GetAccountAsync();
        Assert.Null(account);
    }
}
