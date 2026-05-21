namespace ChildDev.Api.Models.Dtos;

public record SyncRequest<T>(List<T> Records, long LastSyncAt);
public record SyncResponse<T>(List<T> Records);

public record JournalDto(
    string Guid, string AccountFk, string? Notes, string? Activity,
    string? Mood, string? EmotionReason, string? Tags, long EnteredDate, long UpdatedOn, long? DeletedAt);

public record GoalDto(
    string Guid, string AccountFk, string? GoalText, long? NextMeetingDate,
    long? ExpirationDate, long EnteredDate, string? MeasurableOutcome,
    long? CompletionDate, long UpdatedOn, long? DeletedAt);

public record GoalProgressDto(
    string Guid, string AccountFk, string GoalFk, string? NextStepItems,
    long? NextMeetingDate, long UpdatedOn, long? DeletedAt);

public record TodoDto(
    string Guid, string AccountFk, string? Title, string? Notes,
    long? DueDate, long? CompletedAt, long UpdatedOn, long? DeletedAt);
