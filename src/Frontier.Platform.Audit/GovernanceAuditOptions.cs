using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;

namespace Frontier.Platform.Audit;

/// <summary>
/// Governance audit settings, bound from the <c>GovernanceAudit</c> section (ADR-PA30). The append
/// retry is optimistic-concurrency re-hashing, not transient-fault handling: when another writer
/// moved the chain head first, the append re-reads the head, re-hashes and tries again. It is
/// data, never literals, and the defaults are safe for a deployment that sets nothing.
/// </summary>
public sealed class GovernanceAuditOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "GovernanceAudit";

    /// <summary>Total append attempts, the first included.</summary>
    [Range(1, 50)]
    public int AppendMaxAttempts { get; init; } = 8;

    /// <summary>The delay before the first re-attempt, in milliseconds; it doubles per attempt.</summary>
    [Range(0, 10_000)]
    public int AppendBaseDelayMs { get; init; } = 25;

    /// <summary>The cap on any one delay, in milliseconds.</summary>
    [Range(0, 60_000)]
    public int AppendMaxDelayMs { get; init; } = 1_000;
}

/// <summary>The delay between governance append attempts (ADR-PA30).</summary>
internal static class GovernanceAuditBackoff
{
    /// <summary>
    /// <c>min(base × 2^(attempt−1), max)</c>, scaled into its upper half by <paramref name="jitter"/>
    /// in [0, 1) so that contending writers spread out.
    /// </summary>
    internal static TimeSpan DelayFor(int attempt, GovernanceAuditOptions options, double jitter)
    {
        var exponential = options.AppendBaseDelayMs * Math.Pow(2, attempt - 1);
        var capped = Math.Min(exponential, options.AppendMaxDelayMs);
        return TimeSpan.FromMilliseconds(capped * (0.5 + (jitter / 2)));
    }

    /// <summary>A jitter value in [0, 1).</summary>
    internal static double NextJitter() => RandomNumberGenerator.GetInt32(1_000) / 1_000.0;
}

/// <summary>Classifies a transactional batch's status (ADR-PA30).</summary>
internal static class GovernanceAuditConflict
{
    /// <summary>
    /// 412 (the head's ETag moved) and 409 (the sequence, head or record id already exists) mean
    /// another writer got there first; the append re-reads and re-hashes. Anything else is not a
    /// concurrency conflict.
    /// </summary>
    internal static bool IsConcurrencyConflict(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict;
}
