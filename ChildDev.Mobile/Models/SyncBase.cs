using SQLite;

namespace ChildDev.Mobile.Models;

public abstract class SyncBase
{
    [PrimaryKey]
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    public string AccountFk { get; set; } = string.Empty;
    public long UpdatedOn { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long? DeletedAt { get; set; }
}
