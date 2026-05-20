using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.RegularExpressions;
using TunSociety.Api.Data;

namespace TunSociety.Api.Services;

public class AiScoringClient
{
    private const int SimilarityHistoryTake = 180;
    private const string ModerationPolicyVersion = "2026-05-illegal-self-harm-v1";
    private static readonly TimeSpan ExactCacheLifetime = TimeSpan.FromHours(6);
    private static readonly TimeSpan SimilarityCacheLifetime = TimeSpan.FromMinutes(30);

    private readonly LocalAiService _localAiService;
    private readonly ApplicationDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AiScoringClient> _logger;

    public AiScoringClient(
        LocalAiService localAiService,
        ApplicationDbContext dbContext,
        IMemoryCache cache,
        ILogger<AiScoringClient> logger)
    {
        _localAiService = localAiService;
        _dbContext = dbContext;
        _cache = cache;
        _logger = logger;
    }

    public async Task<AiModerationAssessment> AnalyzeAsync(
        string content,
        string contentType = "GenericContent",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return AiModerationAssessment.Empty;
        }

        var normalizedKind = ModerationFingerprint.NormalizeContentKind(contentType);
        var fingerprint = ModerationFingerprint.From(content);

        if (normalizedKind == "profile" && LooksLikeSafeProfileName(content, fingerprint))
        {
            return AiModerationAssessment.Empty;
        }

        if (normalizedKind != "profile" && LooksLikeBenignCasualText(content, fingerprint))
        {
            return AiModerationAssessment.Empty;
        }

        var exactCacheKey = BuildExactCacheKey(normalizedKind, fingerprint.CanonicalText);

        if (_cache.TryGetValue<AiModerationAssessment>(exactCacheKey, out var cachedAssessment))
        {
            return cachedAssessment!;
        }

        var similarAssessment = await TryReuseSimilarAssessmentAsync(fingerprint, normalizedKind, cancellationToken);
        if (similarAssessment is not null)
        {
            _cache.Set(exactCacheKey, similarAssessment, ExactCacheLifetime);
            return similarAssessment;
        }

        var result = await _localAiService.ModerateAsync(content, contentType, cancellationToken);
        var assessment = ToAssessment(result);
        _cache.Set(exactCacheKey, assessment, ExactCacheLifetime);
        return assessment;
    }

    public async Task<double> ScoreAsync(
        string content,
        string contentType = "GenericContent",
        CancellationToken cancellationToken = default)
    {
        var assessment = await AnalyzeAsync(content, contentType, cancellationToken);
        return assessment.Score;
    }

    private async Task<AiModerationAssessment?> TryReuseSimilarAssessmentAsync(
        ModerationFingerprint current,
        string normalizedKind,
        CancellationToken cancellationToken)
    {
        if (current.TokenSet.Count == 0)
        {
            return null;
        }

        var similarityCacheKey = $"moderation:similar:{normalizedKind}:{current.Signature}";
        if (_cache.TryGetValue<AiModerationAssessment>(similarityCacheKey, out var cachedAssessment))
        {
            return cachedAssessment!;
        }

        var recentResults = await _dbContext.ModerationResults
            .AsNoTracking()
            .Where(result => result.Action != "Allow" && !string.IsNullOrWhiteSpace(result.ContentSnapshot))
            .OrderByDescending(result => result.CreatedAtUtc)
            .Take(SimilarityHistoryTake)
            .Select(result => new RecentModerationCandidate(
                result.ContentType,
                result.ContentSnapshot,
                result.Score,
                result.FlagsData))
            .ToListAsync(cancellationToken);

        AiModerationAssessment? bestAssessment = null;
        var bestSimilarity = 0d;

        foreach (var candidate in recentResults)
        {
            if (!ModerationFingerprint.IsCompatibleKind(normalizedKind, ModerationFingerprint.NormalizeContentKind(candidate.ContentType)))
            {
                continue;
            }

            var candidateFingerprint = ModerationFingerprint.From(candidate.ContentSnapshot);
            if (candidateFingerprint.TokenSet.Count == 0)
            {
                continue;
            }

            if (normalizedKind == "profile" && !current.HarmfulTokens.Intersect(candidateFingerprint.HarmfulTokens, StringComparer.OrdinalIgnoreCase).Any())
            {
                continue;
            }

            var similarity = ModerationFingerprint.ComputeSimilarity(current, candidateFingerprint);
            if (similarity < 0.66)
            {
                continue;
            }

            var candidateFlags = ParseFlags(candidate.FlagsData);
            if (candidateFlags.Count == 0)
            {
                continue;
            }

            var harmfulOverlap = current.HarmfulTokens.Intersect(candidateFingerprint.HarmfulTokens, StringComparer.OrdinalIgnoreCase).Any();
            var profanityOverlap = current.AbuseTokens.Intersect(candidateFingerprint.AbuseTokens, StringComparer.OrdinalIgnoreCase).Any();

            if (!harmfulOverlap && !profanityOverlap && similarity < 0.8)
            {
                continue;
            }

            if (similarity <= bestSimilarity)
            {
                continue;
            }

            bestSimilarity = similarity;
            bestAssessment = new AiModerationAssessment(
                Math.Round(Math.Max(candidate.Score, 0.72), 3, MidpointRounding.AwayFromZero),
                candidateFlags);
        }

        if (bestAssessment is not null)
        {
            _logger.LogInformation(
                "Reused cached moderation assessment from similar prior content. Kind={ContentType} Similarity={Similarity:F2}",
                normalizedKind,
                bestSimilarity);
            _cache.Set(similarityCacheKey, bestAssessment, SimilarityCacheLifetime);
        }

        return bestAssessment;
    }

    private static AiModerationAssessment ToAssessment(LocalAiModerationResult result)
    {
        var flags = result.Categories
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Select(category => category.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var score = NormalizeScore(result.Decision, result.Confidence, flags.Count);
        return new AiModerationAssessment(score, flags);
    }

    private static string BuildExactCacheKey(string contentType, string canonicalText)
        => $"moderation:exact:{ModerationPolicyVersion}:{contentType}:{canonicalText}";

    private static bool LooksLikeSafeProfileName(string content, ModerationFingerprint fingerprint)
    {
        if (fingerprint.HarmfulTokens.Count > 0 || fingerprint.AbuseTokens.Count > 0)
        {
            return false;
        }

        var trimmed = content.Trim();
        if (trimmed.Length is < 2 or > 60)
        {
            return false;
        }

        if (trimmed.Contains('@') || trimmed.Contains("http", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(trimmed, @"\d"))
        {
            return false;
        }

        return Regex.IsMatch(trimmed, @"^[\p{L}\p{M}][\p{L}\p{M}'\-\s]{1,59}$", RegexOptions.CultureInvariant);
    }

    private static bool LooksLikeBenignCasualText(string content, ModerationFingerprint fingerprint)
    {
        if (fingerprint.HarmfulTokens.Count > 0 || fingerprint.AbuseTokens.Count > 0)
        {
            return false;
        }

        var trimmed = content.Trim();
        if (trimmed.Length is < 2 or > 80)
        {
            return false;
        }

        if (trimmed.Contains("http", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("www.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Regex.IsMatch(trimmed, @"\b(buy\s+now|click\s+here|limited\s+offer|subscribe\s+now|send\s+money|wire\s+money|free\s+money|earn\s+money|make\s+money\s+fast|crypto\s+giveaway|investment\s+guaranteed|dm\s+me|dm\s+for\s+prices|message\s+me\s+privately\s+for\s+prices|whatsapp\s+me|telegram)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        var words = Regex.Matches(trimmed, @"[\p{L}\p{M}][\p{L}\p{M}'\-]*").Count;
        return words is > 0 and <= 6;
    }

    private static List<string> ParseFlags(string flagsData)
    {
        if (string.IsNullOrWhiteSpace(flagsData))
        {
            return [];
        }

        return flagsData
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(flag => !string.IsNullOrWhiteSpace(flag))
            .Select(flag => flag.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(flag => flag, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static double NormalizeScore(string decision, double confidence, int categoryCount)
    {
        var normalizedDecision = decision?.Trim().ToUpperInvariant() ?? "ALLOW";
        var normalizedConfidence = Math.Clamp(confidence, 0, 1);

        var score = normalizedDecision switch
        {
            "BLOCK" => Math.Max(normalizedConfidence, 0.9),
            "FLAG" => Math.Max(normalizedConfidence, 0.7),
            _ => categoryCount > 0 ? Math.Max(normalizedConfidence, 0.35) : normalizedConfidence * 0.2
        };

        return Math.Round(Math.Clamp(score, 0, 1), 3, MidpointRounding.AwayFromZero);
    }
}

public sealed record AiModerationAssessment(double Score, IReadOnlyList<string> Flags)
{
    public static AiModerationAssessment Empty { get; } = new(0, Array.Empty<string>());
}

internal sealed record RecentModerationCandidate(
    string ContentType,
    string ContentSnapshot,
    double Score,
    string FlagsData);

internal sealed class ModerationFingerprint
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "at", "be", "for", "from", "i", "in", "is", "it", "me", "my", "of", "on", "or",
        "our", "that", "the", "their", "them", "this", "to", "u", "ur", "we", "you", "your"
    };

    private static readonly Dictionary<string, string> TokenAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["assholes"] = "asshole",
        ["bitches"] = "bitch",
        ["fucker"] = "fuck",
        ["fucking"] = "fuck",
        ["fucked"] = "fuck",
        ["fvck"] = "fuck",
        ["f*ck"] = "fuck",
        ["drugs"] = "drug",
        ["idiots"] = "idiot",
        ["killing"] = "kill",
        ["killed"] = "kill",
        ["morons"] = "moron",
        ["murdering"] = "murder",
        ["narcotics"] = "narcotic",
        ["nudes"] = "nude",
        ["pills"] = "pill",
        ["pornographic"] = "pornography",
        ["rapists"] = "rapist",
        ["scammers"] = "scam",
        ["scamming"] = "scam",
        ["suicidal"] = "suicide",
        ["stfu"] = "shutup",
        ["threatening"] = "threat",
        ["threats"] = "threat"
    };

    private static readonly HashSet<string> HarmfulLexicon = new(StringComparer.OrdinalIgnoreCase)
    {
        "abuse", "asshole", "bastard", "bitch", "bomb", "cocaine", "die", "drug", "fentanyl", "fraud",
        "fuck", "harm", "hate", "heroin", "hit", "hurt", "idiot", "illegal", "kill", "mdma", "meth",
        "moron", "murder", "narcotic", "nude", "pill", "porn", "pornography", "racism", "racist", "scam",
        "selfharm", "shutup", "slur", "spam", "stupid", "suicide", "threat", "violence", "worthless", "xanax"
    };

    public string CanonicalText { get; }
    public string Signature { get; }
    public HashSet<string> TokenSet { get; }
    public HashSet<string> HarmfulTokens { get; }
    public HashSet<string> AbuseTokens { get; }

    private ModerationFingerprint(
        string canonicalText,
        HashSet<string> tokenSet,
        HashSet<string> harmfulTokens,
        HashSet<string> abuseTokens)
    {
        CanonicalText = canonicalText;
        TokenSet = tokenSet;
        HarmfulTokens = harmfulTokens;
        AbuseTokens = abuseTokens;
        Signature = string.Join("|", tokenSet.OrderBy(token => token, StringComparer.OrdinalIgnoreCase));
    }

    public static ModerationFingerprint From(string content)
    {
        var normalized = NormalizeText(content);
        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeToken)
            .Where(token => token.Length > 1 && !StopWords.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var canonical = string.Join(' ', tokens.OrderBy(token => token, StringComparer.OrdinalIgnoreCase));
        var harmful = tokens.Where(token => HarmfulLexicon.Contains(token)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var abuse = tokens.Where(token => token is "fuck" or "asshole" or "bastard" or "bitch" or "idiot" or "moron" or "stupid" or "worthless" or "shutup")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new ModerationFingerprint(canonical, tokens, harmful, abuse);
    }

    public static string NormalizeContentKind(string? contentType)
    {
        var normalized = contentType?.Trim().ToLowerInvariant() ?? "generic";
        return normalized switch
        {
            "post" => "post",
            "comment" => "comment",
            "postcomment" => "comment",
            "message" => "message",
            "directmessage" => "message",
            "profile" => "profile",
            "user" => "profile",
            _ => normalized
        };
    }

    public static bool IsCompatibleKind(string current, string candidate)
        => current == candidate || current == "message" && candidate == "comment" || current == "comment" && candidate == "message";

    public static double ComputeSimilarity(ModerationFingerprint current, ModerationFingerprint candidate)
    {
        if (current.CanonicalText.Length > 0 && current.CanonicalText.Equals(candidate.CanonicalText, StringComparison.Ordinal))
        {
            return 1;
        }

        var tokenOverlap = current.TokenSet.Intersect(candidate.TokenSet, StringComparer.OrdinalIgnoreCase).Count();
        var union = current.TokenSet.Union(candidate.TokenSet, StringComparer.OrdinalIgnoreCase).Count();
        var jaccard = union == 0 ? 0 : (double)tokenOverlap / union;

        var harmfulOverlap = current.HarmfulTokens.Intersect(candidate.HarmfulTokens, StringComparer.OrdinalIgnoreCase).Count();
        var harmfulUnion = current.HarmfulTokens.Union(candidate.HarmfulTokens, StringComparer.OrdinalIgnoreCase).Count();
        var harmfulJaccard = harmfulUnion == 0 ? 0 : (double)harmfulOverlap / harmfulUnion;

        var prefixBonus = current.CanonicalText.Length > 0 && candidate.CanonicalText.Length > 0 &&
            (current.CanonicalText.Contains(candidate.CanonicalText, StringComparison.Ordinal) ||
             candidate.CanonicalText.Contains(current.CanonicalText, StringComparison.Ordinal))
            ? 0.08
            : 0;

        return Math.Min(1, (jaccard * 0.65) + (harmfulJaccard * 0.35) + prefixBonus);
    }

    private static string NormalizeText(string content)
    {
        var lowered = content.ToLowerInvariant()
            .Replace('@', 'a')
            .Replace('$', 's')
            .Replace('€', 'e')
            .Replace('£', 'l')
            .Replace('0', 'o')
            .Replace('1', 'i')
            .Replace('3', 'e')
            .Replace('4', 'a')
            .Replace('5', 's')
            .Replace('7', 't');

        lowered = System.Text.RegularExpressions.Regex.Replace(lowered, @"(.)\1{2,}", "$1$1");
        lowered = System.Text.RegularExpressions.Regex.Replace(lowered, @"[^a-z\s]", " ");
        lowered = System.Text.RegularExpressions.Regex.Replace(lowered, @"\s+", " ").Trim();
        return lowered;
    }

    private static string NormalizeToken(string token)
    {
        if (TokenAliases.TryGetValue(token, out var alias))
        {
            return alias;
        }

        var stemmed = token;
        if (stemmed.EndsWith("ing", StringComparison.OrdinalIgnoreCase) && stemmed.Length > 5)
        {
            stemmed = stemmed[..^3];
        }
        else if (stemmed.EndsWith("ed", StringComparison.OrdinalIgnoreCase) && stemmed.Length > 4)
        {
            stemmed = stemmed[..^2];
        }
        else if (stemmed.EndsWith("es", StringComparison.OrdinalIgnoreCase) && stemmed.Length > 4)
        {
            stemmed = stemmed[..^2];
        }
        else if (stemmed.EndsWith('s') && stemmed.Length > 4)
        {
            stemmed = stemmed[..^1];
        }

        return TokenAliases.TryGetValue(stemmed, out var stemAlias) ? stemAlias : stemmed;
    }
}
