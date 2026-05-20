using Microsoft.EntityFrameworkCore;
using TunSociety.Api.Models;
using TunSociety.Api.Data;

namespace TunSociety.Api.Services;

public class SanctionService
{
    private const int FreezeThreshold = 3;

    private readonly ApplicationDbContext _dbContext;

    public SanctionService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SanctionOutcome> EvaluateAsync(User user, ModerationResult result, CancellationToken cancellationToken = default)
    {
        Warning? warning = null;
        Freeze? freeze = null;
        var updatedUser = false;
        var warningCount = await _dbContext.Warnings.CountAsync(current => current.UserId == user.Id, cancellationToken);
        var suppressionCount = await _dbContext.ModerationResults
            .CountAsync(current => current.UserId == user.Id && current.Action == "Block", cancellationToken);

        if (result.Action == "Block")
        {
            warning = new Warning
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Reason = BuildWarningReason(result, warningCount + 1),
                IssuedAtUtc = DateTime.UtcNow
            };

            warningCount += 1;
            suppressionCount += 1;
        }

        var hasActiveFreeze = user.IsFrozen || await _dbContext.Freezes
            .AnyAsync(current => current.UserId == user.Id && current.IsActive, cancellationToken);

        if (result.Action == "Block" && warningCount >= FreezeThreshold && suppressionCount >= FreezeThreshold && !hasActiveFreeze)
        {
            user.IsFrozen = true;
            updatedUser = true;
            freeze = new Freeze
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Reason = $"Account frozen after {FreezeThreshold} suppressed policy violations. Latest reason: {result.Reason ?? "Content blocked by moderation policy."}",
                StartsAtUtc = DateTime.UtcNow,
                IsActive = true
            };
        }

        return new SanctionOutcome(
            warning,
            freeze,
            updatedUser,
            warningCount,
            suppressionCount,
            Math.Max(0, FreezeThreshold - Math.Max(warningCount, suppressionCount)),
            user.IsFrozen);
    }

    private static string BuildWarningReason(ModerationResult result, int warningCount)
    {
        return $"Warning {warningCount} of {FreezeThreshold}. {result.Reason ?? "Content blocked by moderation policy."}";
    }
}

public record SanctionOutcome(
    Warning? Warning,
    Freeze? Freeze,
    bool UserUpdated,
    int WarningCount,
    int SuppressionCount,
    int RemainingViolationsBeforeFreeze,
    bool AccountFrozen);
