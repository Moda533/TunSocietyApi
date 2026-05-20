using System.ComponentModel.DataAnnotations.Schema;

namespace TunSociety.Api.Models;

public class ModerationResult
{
    public Guid Id { get; set; }
    public Guid ContentId { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string ContentSnapshot { get; set; } = string.Empty;
    public double Score { get; set; }
    public string Action { get; set; } = "Allow";
    public string? Reason { get; set; }
    public bool IsEscalated { get; set; }
    public DateTime? EscalatedAtUtc { get; set; }
    public string? EscalationNote { get; set; }
    public string FlagsData { get; set; } = string.Empty;

    [NotMapped]
    public List<string> Flags
    {
        get => string.IsNullOrWhiteSpace(FlagsData)
            ? []
            : FlagsData
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(flag => flag, StringComparer.OrdinalIgnoreCase)
                .ToList();
        set => FlagsData = value is null || value.Count == 0
            ? string.Empty
            : string.Join(
                ',',
                value
                    .Where(flag => !string.IsNullOrWhiteSpace(flag))
                    .Select(flag => flag.Trim().ToLowerInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(flag => flag, StringComparer.OrdinalIgnoreCase));
    }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
