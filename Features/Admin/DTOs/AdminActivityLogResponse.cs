namespace TunSociety.Api.DTOs.Admin;

public sealed class AdminActivityLogResponse
{
    public Guid Id { get; init; }

    public Guid? ActorUserId { get; init; }

    public string? ActorDisplayName { get; init; }

    public string? ActorEmail { get; init; }

    public Guid? SubjectUserId { get; init; }

    public string? SubjectDisplayName { get; init; }

    public string? SubjectEmail { get; init; }

    public string Category { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string ActionLabel { get; init; } = string.Empty;

    public string EntityType { get; init; } = string.Empty;

    public string EntityId { get; init; } = string.Empty;

    public string? TargetDisplayName { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string? Data { get; init; }

    public IReadOnlyDictionary<string, string?> Metadata { get; init; } = new Dictionary<string, string?>();

    public DateTime CreatedAtUtc { get; init; }
}
