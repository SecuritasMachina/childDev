using System.ComponentModel.DataAnnotations;

namespace ChildDev.Api.Models.Entities;

public class Goal
{
    [Key, MaxLength(36)]
    public string Guid { get; set; } = string.Empty;
    [Required, MaxLength(36)]
    public string AccountFk { get; set; } = string.Empty;
    public string? GoalText { get; set; }
    public long? NextMeetingDate { get; set; }
    public long? ExpirationDate { get; set; }
    public long EnteredDate { get; set; }
    public string? MeasurableOutcome { get; set; }
    public long? CompletionDate { get; set; }
    public int? ProgressPercent { get; set; }
    [MaxLength(50)]
    public string? Category { get; set; }
    public bool IsPinned { get; set; }
    public long UpdatedOn { get; set; }
    public long? DeletedAt { get; set; }
}
