using SQLite;

namespace LevelUp.Models;

public class Journal : SyncBase
{
    public string? Notes { get; set; }
    public string? Activity { get; set; }
    public string? Mood { get; set; }
    public string? EmotionReason { get; set; }
    public string? Tags { get; set; }
    public long EnteredDate { get; set; }

    [Ignore]
    public string DisplayText => Notes ?? Activity ?? string.Empty;
}
