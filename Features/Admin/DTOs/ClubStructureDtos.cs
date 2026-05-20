namespace TunSociety.Api.DTOs.Admin;

public sealed class DepartmentResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public Guid CreatedById { get; init; }
    public string? CreatedByName { get; init; }
    public bool IsArchived { get; init; }
    public int UserCount { get; init; }
}

public sealed class DepartmentRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class BadgeResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Guid? DepartmentId { get; init; }
    public string? DepartmentName { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public bool IsArchived { get; init; }
    public bool IsDefault { get; init; }
    public int UserCount { get; init; }
}

public sealed class BadgeRequest
{
    public string Name { get; set; } = string.Empty;
    public Guid? DepartmentId { get; set; }
}

public sealed class UpdateUserMembershipRequest
{
    public Guid? DepartmentId { get; set; }
    public Guid? BadgeId { get; set; }
}
