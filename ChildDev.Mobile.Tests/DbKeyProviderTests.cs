using LevelUp.Services;
using Xunit;

namespace LevelUp.Tests;

public class DbKeyProviderTests
{
    [Fact]
    public async Task InMemoryProvider_ReturnsStableNonEmptyKey()
    {
        IDbKeyProvider p = new InMemoryDbKeyProvider();
        var k1 = await p.GetKeyAsync();
        var k2 = await p.GetKeyAsync();
        Assert.False(string.IsNullOrWhiteSpace(k1));
        Assert.Equal(k1, k2);
    }
}
