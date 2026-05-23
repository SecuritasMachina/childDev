using SQLite;

namespace LevelUp.Models;

public class GoalProgress : SyncBase
{
    [Indexed]
    public string GoalFk { get; set; } = string.Empty;
    public string? NextStepItems { get; set; }
    public long? NextMeetingDate { get; set; }
}
