using SQLite;

namespace LevelUp.Models;

public class Reminder
{
    [PrimaryKey]
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    [Indexed]
    public string AccountFk { get; set; } = string.Empty;
    public string Topic { get; set; } = "General"; // "Goal", "Journal", "Todo", "General"
    public string? EntityGuid { get; set; }         // null for topic-level reminders
    public string Title { get; set; } = string.Empty;
    public string? EntityLabel { get; set; }        // display label, e.g. goal text snippet
    public long FireAt { get; set; }                // Unix milliseconds
    public bool IsDismissed { get; set; }
    public int NotificationId { get; set; }         // used to cancel/update the OS notification
}
