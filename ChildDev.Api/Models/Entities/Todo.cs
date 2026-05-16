using System.ComponentModel.DataAnnotations;

namespace ChildDev.Api.Models.Entities;

public class Todo
{
    [Key, MaxLength(36)]
    public string Guid { get; set; } = string.Empty;
    [Required, MaxLength(36)]
    public string AccountFk { get; set; } = string.Empty;
    [MaxLength(500)]
    public string? Title { get; set; }
    public string? Notes { get; set; }
    public long? DueDate { get; set; }
    public long? CompletedAt { get; set; }
    public long UpdatedOn { get; set; }
    public long? DeletedAt { get; set; }
}
