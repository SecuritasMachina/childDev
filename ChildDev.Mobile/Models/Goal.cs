using SQLite;

namespace ChildDev.Mobile.Models;

public class Goal : SyncBase
{
    public string? GoalText { get; set; }
    public long? NextMeetingDate { get; set; }
    public long? ExpirationDate { get; set; }
    public long EnteredDate { get; set; }
    public string? MeasurableOutcome { get; set; }
    public long? CompletionDate { get; set; }

    [Ignore]
    public string? LatestNextStepItems { get; set; }

    [Ignore]
    public long? LatestProgressAt { get; set; }

    [Ignore]
    public bool ShowNoNotesYet => LatestProgressAt is null && CompletionDate is null;
}
