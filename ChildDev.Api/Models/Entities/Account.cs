using System.ComponentModel.DataAnnotations;

namespace ChildDev.Api.Models.Entities;

public class Account
{
    [Key, MaxLength(36)]
    public string Guid { get; set; } = string.Empty;
    [Required, MaxLength(100)]
    public string NickName { get; set; } = string.Empty;
    [Required, MaxLength(100)]
    public string PinHash { get; set; } = string.Empty;
    public long CreatedOn { get; set; }
    [MaxLength(200)]
    public string? Email { get; set; }
    public bool AlertGoalComplete { get; set; }
}
