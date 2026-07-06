namespace LevelUp.Services;

/// <summary>Returns a caller-supplied fixed SQLCipher key. For tests and NO_MAUI builds where the
/// key is known up front (not sourced from SecureStorage). NOT for production devices.</summary>
public sealed class FixedKeyDbKeyProvider : IDbKeyProvider
{
    private readonly string _key;
    public FixedKeyDbKeyProvider(string key) => _key = key;
    public Task<string> GetKeyAsync() => Task.FromResult(_key);
}
