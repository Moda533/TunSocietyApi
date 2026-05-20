namespace TunSociety.Api.DTOs.Admin;

public sealed class AdminActivityFeedResponse
{
    public IReadOnlyList<AdminActivityLogResponse> Items { get; init; } = Array.Empty<AdminActivityLogResponse>();

    public int TotalCount { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }
}
