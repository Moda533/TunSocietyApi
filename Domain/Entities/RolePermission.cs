namespace TunSociety.Api.Models;

public class RolePermission
{
    public Guid Id { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Permission { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
