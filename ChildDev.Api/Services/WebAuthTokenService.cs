using System.Collections.Concurrent;

namespace ChildDev.Api.Services;

public class WebAuthTokenService
{
    private readonly ConcurrentDictionary<string, (string AccountGuid, string NickName, long ExpiryMs)> _tokens = new();

    public string GenerateToken(string accountGuid, string nickName)
    {
        var token = Guid.NewGuid().ToString("N");
        _tokens[token] = (accountGuid, nickName, DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds());
        return token;
    }

    public (string AccountGuid, string NickName)? ConsumeToken(string token)
    {
        if (_tokens.TryRemove(token, out var value) &&
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < value.ExpiryMs)
            return (value.AccountGuid, value.NickName);
        return null;
    }
}
