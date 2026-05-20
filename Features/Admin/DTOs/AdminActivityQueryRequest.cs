namespace TunSociety.Api.DTOs.Admin;

public sealed class AdminActivityQueryRequest
{
    public string? Query { get; init; }

    public Guid? UserId { get; init; }

    public string? UserQuery { get; init; }

    public string? Category { get; init; }

    public DateTime? FromUtc { get; init; }

    public DateTime? ToUtc { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
