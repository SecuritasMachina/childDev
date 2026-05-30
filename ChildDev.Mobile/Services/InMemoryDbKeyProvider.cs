using System.Security.Cryptography;

namespace LevelUp.Services;

/// <summary>Non-persistent key provider for tests / NO_MAUI builds. NOT for production devices.</summary>
public sealed class InMemoryDbKeyProvider : IDbKeyProvider
{
    private readonly string _key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public Task<string> GetKeyAsync() => Task.FromResult(_key);
}
