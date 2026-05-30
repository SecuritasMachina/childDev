namespace LevelUp.Services;

/// <summary>Supplies the SQLCipher passphrase for the local database.</summary>
public interface IDbKeyProvider
{
    Task<string> GetKeyAsync();
}
