using System.ComponentModel.DataAnnotations;

namespace ChildDev.Api.Models.Entities;

public class Journal
{
    [Key, MaxLength(36)]
    public string Guid { get; set; } = string.Empty;
    [Required, MaxLength(36)]
    public string AccountFk { get; set; } = string.Empty;
    public string? Notes { get; set; }
    [MaxLength(255)]
    public string? Activity { get; set; }
    [MaxLength(50)]
    public string? Mood { get; set; }
    [MaxLength(500)]
    public string? Tags { get; set; }
    public long EnteredDate { get; set; }
    public long UpdatedOn { get; set; }
    public long? DeletedAt { get; set; }
}
