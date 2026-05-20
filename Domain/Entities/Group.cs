namespace TunSociety.Api.Models;

public class Group
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public User? OwnerUser { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public string Visibility { get; set; } = "Public";
    public int MemberCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
