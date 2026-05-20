using TunSociety.Api.Models;
using TunSociety.Api.Services;

namespace TunSociety.Api.DTOs.Moderation;

public class ModerationFeedbackResponse
{
    public double Score { get; set; }
    public List<string> Flags { get; set; } = [];
    public string Action { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public bool IsSuppressed { get; set; }
    public int WarningCount { get; set; }
    public int SuppressionCount { get; set; }
    public int RemainingViolationsBeforeFreeze { get; set; }
    public bool AccountFrozen { get; set; }

    public static ModerationFeedbackResponse From(ModerationResult result, SanctionOutcome? outcome = null)
    {
        return new ModerationFeedbackResponse
        {
            Score = result.Score,
            Flags = [.. result.Flags],
            Action = result.Action,
            Reason = result.Reason,
            IsSuppressed = result.Action != "Allow",
            WarningCount = outcome?.WarningCount ?? 0,
            SuppressionCount = outcome?.SuppressionCount ?? 0,
            RemainingViolationsBeforeFreeze = outcome?.RemainingViolationsBeforeFreeze ?? 0,
            AccountFrozen = outcome?.AccountFrozen ?? false
        };
    }
}
