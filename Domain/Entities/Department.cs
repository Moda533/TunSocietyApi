namespace TunSociety.Api.Models;

public class Department
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid CreatedById { get; set; }
    public User? CreatedBy { get; set; }
    public bool IsArchived { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<UserBadge> Badges { get; set; } = new List<UserBadge>();
}
