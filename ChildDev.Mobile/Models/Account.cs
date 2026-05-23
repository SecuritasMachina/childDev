using SQLite;

namespace LevelUp.Models;

public class Account
{
    [PrimaryKey]
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    public string NickName { get; set; } = string.Empty;
    public string PinHash { get; set; } = string.Empty;
    public long CreatedOn { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long LastSyncAt { get; set; } = 0;
    public string? ServerJwt { get; set; }
    public string? ServerUrl { get; set; }
}
