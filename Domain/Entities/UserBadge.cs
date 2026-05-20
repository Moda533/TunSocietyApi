namespace TunSociety.Api.Models;

public class UserBadge
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsArchived { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
}
