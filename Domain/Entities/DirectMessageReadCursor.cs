namespace TunSociety.Api.Models;

public class DirectMessageReadCursor
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid PartnerUserId { get; set; }
    public User? PartnerUser { get; set; }

    public Guid? LastVisibleMessageId { get; set; }
    public DateTime? LastVisibleMessageAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
