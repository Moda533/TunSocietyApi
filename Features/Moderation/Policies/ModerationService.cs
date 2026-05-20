using TunSociety.Api.Models;

namespace TunSociety.Api.Services;

public class ModerationService
{
    private static readonly string[] FlagPriority =
    {
        "racism",
        "hate",
        "illegal-goods",
        "pornography",
        "scam",
        "threat",
        "violence",
        "self-harm",
        "abuse",
        "spam",
        "political"
    };

    private static readonly HashSet<string> BlockFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "violence",
        "threat",
        "hate",
        "illegal-goods",
        "racism",
        "pornography",
        "scam"
    };

    private static readonly HashSet<string> ReviewFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "abuse",
        "self-harm",
        "spam",
        "political"
    };

    private readonly AiScoringClient _aiScoringClient;

    public ModerationService(AiScoringClient aiScoringClient)
    {
        _aiScoringClient = aiScoringClient;
    }

    public async Task<ModerationResult> EvaluateAsync(
        Guid contentId,
        string content,
        string contentType = "GenericContent",
        CancellationToken cancellationToken = default)
    {
        var assessment = await _aiScoringClient.AnalyzeAsync(content, contentType, cancellationToken);
        var flags = assessment.Flags
            .Where(flag => !string.IsNullOrWhiteSpace(flag))
            .Select(flag => flag.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(flag => flag, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var action = DetermineAction(assessment.Score, flags);
        var reason = BuildReason(action, assessment.Score, flags);

        return new ModerationResult
        {
            Id = Guid.NewGuid(),
            ContentId = contentId,
            Score = assessment.Score,
            Action = action,
            Reason = reason,
            IsEscalated = false,
            EscalatedAtUtc = null,
            EscalationNote = null,
            Flags = flags,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private static string DetermineAction(double score, IReadOnlyCollection<string> flags)
    {
        var hasSelfHarmOnlyReviewFlag = flags.Contains("self-harm", StringComparer.OrdinalIgnoreCase)
            && !flags.Any(BlockFlags.Contains);

        if (flags.Any(BlockFlags.Contains) || (score >= 0.95 && !hasSelfHarmOnlyReviewFlag))
        {
            return "Block";
        }

        if (flags.Any(ReviewFlags.Contains) || score >= 0.7)
        {
            return "Flag";
        }

        return "Allow";
    }

    private static string? BuildReason(string action, double score, IReadOnlyCollection<string> flags)
    {
        if (action == "Allow")
        {
            return null;
        }

        var primaryFlag = GetPrimaryFlag(flags);
        if (primaryFlag != null)
        {
            return action switch
            {
                "Block" => primaryFlag switch
                {
                    "hate" => "Content blocked because hateful content is not allowed in TunSociety.",
                    "illegal-goods" => "Content blocked because illegal goods or drug-sale content is not allowed in TunSociety.",
                    "racism" => "Content blocked because racist content is not allowed in TunSociety.",
                    "pornography" => "Content blocked because pornographic content is not allowed in TunSociety.",
                    "scam" => "Content blocked because scam or fraud content is not allowed in TunSociety.",
                    "threat" => "Content blocked because threats are not allowed in TunSociety.",
                    "violence" => "Content blocked because violent content is not allowed in TunSociety.",
                    _ => $"Content blocked due to moderation category: {primaryFlag}."
                },
                "Flag" => primaryFlag switch
                {
                    "abuse" => "Content flagged for moderator review because it may be abusive.",
                    "self-harm" => "Content flagged for moderator review because it may describe self-harm or a crisis situation.",
                    "spam" => "Content flagged for moderator review because it appears to be spam.",
                    "political" => "Content flagged for moderator review because it may need a human check.",
                    _ => $"Content flagged for moderator review due to category: {primaryFlag}."
                },
                _ => null
            };
        }

        return action == "Block"
            ? $"Content blocked because score {score:F3} exceeded the moderation threshold."
            : $"Content flagged for moderator review because score {score:F3} exceeded the review threshold.";
    }

    private static string? GetPrimaryFlag(IReadOnlyCollection<string> flags)
    {
        foreach (var candidate in FlagPriority)
        {
            if (flags.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return flags.FirstOrDefault();
    }
}
