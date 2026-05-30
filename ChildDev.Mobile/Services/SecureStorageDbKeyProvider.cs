#if !NO_MAUI
using System.Security.Cryptography;
using Microsoft.Maui.Storage;

namespace LevelUp.Services;

/// <summary>Stores/loads a per-device SQLCipher key in MAUI SecureStorage (Android Keystore-backed).</summary>
public sealed class SecureStorageDbKeyProvider : IDbKeyProvider
{
    private const string KeyName = "levelup_db_key";

    public async Task<string> GetKeyAsync()
    {
        var existing = await SecureStorage.Default.GetAsync(KeyName);
        if (!string.IsNullOrWhiteSpace(existing)) return existing;

        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await SecureStorage.Default.SetAsync(KeyName, key);
        return key;
    }
}
#endif
