using System.ComponentModel.DataAnnotations;

namespace ChildDev.Api.Models.Entities;

public class Reminder
{
    [Key]
    public int Id { get; set; }
    [Required, MaxLength(36)]
    public string AccountGuid { get; set; } = string.Empty;
    [MaxLength(20)]
    public string Topic { get; set; } = "General";
    [MaxLength(36)]
    public string? EntityGuid { get; set; }
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;
    [MaxLength(200)]
    public string? EntityLabel { get; set; }
    public DateTime FireAt { get; set; }
    public bool IsDismissed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
