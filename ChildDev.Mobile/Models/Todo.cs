namespace ChildDev.Mobile.Models;

public class Todo : SyncBase
{
    public string? Title { get; set; }
    public string? Notes { get; set; }
    public long? DueDate { get; set; }
    public long? CompletedAt { get; set; }
}
