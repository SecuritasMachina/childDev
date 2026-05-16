namespace ChildDev.Mobile.Models;

public class GoalProgress : SyncBase
{
    public string GoalFk { get; set; } = string.Empty;
    public string? NextStepItems { get; set; }
    public long? NextMeetingDate { get; set; }
}
