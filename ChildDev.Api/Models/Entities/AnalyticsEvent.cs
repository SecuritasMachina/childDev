using System.ComponentModel.DataAnnotations;

namespace ChildDev.Api.Models.Entities;

public class AnalyticsEvent
{
    public long Id { get; set; }

    [MaxLength(100)]
    public string EventName { get; set; } = string.Empty;

    public long Timestamp { get; set; }

    [MaxLength(100)]
    public string? AccountGuid { get; set; }

    [MaxLength(50)]
    public string? Page { get; set; }

    [MaxLength(100)]
    public string? Context { get; set; }
}
