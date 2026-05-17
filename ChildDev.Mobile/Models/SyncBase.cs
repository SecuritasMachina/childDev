using SQLite;

namespace ChildDev.Mobile.Models;

public abstract class SyncBase
{
    [PrimaryKey]
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    [Indexed]
    public string AccountFk { get; set; } = string.Empty;
    [Indexed]
    public long UpdatedOn { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    [Indexed]
    public long? DeletedAt { get; set; }
}
