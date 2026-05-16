using System.ComponentModel.DataAnnotations;

namespace ChildDev.Api.Models.Entities;

public class GoalProgress
{
    [Key, MaxLength(36)]
    public string Guid { get; set; } = string.Empty;
    [Required, MaxLength(36)]
    public string AccountFk { get; set; } = string.Empty;
    [Required, MaxLength(36)]
    public string GoalFk { get; set; } = string.Empty;
    public string? NextStepItems { get; set; }
    public long? NextMeetingDate { get; set; }
    public long UpdatedOn { get; set; }
    public long? DeletedAt { get; set; }
}
