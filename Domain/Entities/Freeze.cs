namespace TunSociety.Api.Models;

public class Freeze
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndsAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
}
