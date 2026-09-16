using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// Model-role governance settings, bound from the <c>ModelRoleGovernance</c> section (ADR-PA32).
/// The decision retry is optimistic-concurrency re-allocation, not transient-fault handling: when
/// another approval took the version or moved the proposal first, the decision re-reads, re-allocates
/// and tries again. Retry policy is data, never literals (K7) — the same posture as
/// <c>GovernanceAuditOptions</c> (ADR-PA30) and <c>ExecutionAuditOptions</c> (ADR-PA31), and the
/// defaults are safe for a deployment that sets nothing.
/// </summary>
public sealed class MappingGovernanceOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "ModelRoleGovernance";

    /// <summary>Total decision attempts, the first included.</summary>
    [Range(1, 50)]
    public int DecisionMaxAttempts { get; init; } = 8;

    /// <summary>The delay before the first re-attempt, in milliseconds; it doubles per attempt.</summary>
    [Range(0, 10_000)]
    public int DecisionBaseDelayMs { get; init; } = 25;

    /// <summary>The cap on any one delay, in milliseconds.</summary>
    [Range(0, 60_000)]
    public int DecisionMaxDelayMs { get; init; } = 1_000;
}

/// <summary>
/// The delay between governance decision attempts (ADR-PA32). The curve is ADR-PA30's, reimplemented
/// rather than referenced: this library is governance-tier and may not reference
/// <c>Frontier.Platform.Audit</c> (ADR-PA5), and duplicating eight lines is the cheaper price.
/// </summary>
internal static class MappingGovernanceBackoff
{
    /// <summary>
    /// <c>min(base × 2^(attempt−1), max)</c>, scaled into its upper half by <paramref name="jitter"/>
    /// in [0, 1) so that contending writers spread out rather than colliding again together.
    /// </summary>
    internal static TimeSpan DelayFor(int attempt, MappingGovernanceOptions options, double jitter)
    {
        var exponential = options.DecisionBaseDelayMs * Math.Pow(2, attempt - 1);
        var capped = Math.Min(exponential, options.DecisionMaxDelayMs);
        return TimeSpan.FromMilliseconds(capped * (0.5 + (jitter / 2)));
    }

    /// <summary>A jitter value in [0, 1).</summary>
    internal static double NextJitter() => RandomNumberGenerator.GetInt32(1_000) / 1_000.0;
}
