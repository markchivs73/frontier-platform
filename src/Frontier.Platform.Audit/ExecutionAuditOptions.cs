using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;

namespace Frontier.Platform.Audit;

/// <summary>
/// Execution-audit settings, bound from the <c>ExecutionAudit</c> section (ADR-PA31). The append
/// retry is optimistic-concurrency re-hashing, not transient-fault handling: when another execution
/// on the same engagement moved the chain head first, the append re-reads the head, re-hashes and
/// tries again. It is data, never literals, and the defaults are safe for a deployment that sets
/// nothing. It mirrors <see cref="GovernanceAuditOptions"/>, which set the precedent in this package.
/// </summary>
public sealed class ExecutionAuditOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "ExecutionAudit";

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

/// <summary>The delay between conditional audit append attempts, shared by both chains (ADR-PA30, ADR-PA31).</summary>
internal static class AuditAppendBackoff
{
    /// <summary>
    /// <c>min(base × 2^(attempt−1), max)</c>, scaled into its upper half by <paramref name="jitter"/>
    /// in [0, 1) so that contending writers spread out.
    /// </summary>
    internal static TimeSpan DelayFor(int attempt, int baseDelayMs, int maxDelayMs, double jitter)
    {
        var exponential = baseDelayMs * Math.Pow(2, attempt - 1);
        var capped = Math.Min(exponential, maxDelayMs);
        return TimeSpan.FromMilliseconds(capped * (0.5 + (jitter / 2)));
    }

    /// <summary>The delay before re-attempting an execution-chain append.</summary>
    internal static TimeSpan DelayFor(int attempt, ExecutionAuditOptions options, double jitter) =>
        DelayFor(attempt, options.AppendBaseDelayMs, options.AppendMaxDelayMs, jitter);

    /// <summary>A jitter value in [0, 1).</summary>
    internal static double NextJitter() => RandomNumberGenerator.GetInt32(1_000) / 1_000.0;
}

/// <summary>Classifies an execution-chain transactional batch's status (ADR-PA31).</summary>
internal static class AuditChainConflict
{
    /// <summary>
    /// 412 (the head's ETag moved) and 409 (the head or the record document already exists) mean
    /// another close got there first; the append re-reads and re-hashes. Anything else is not a
    /// concurrency conflict.
    /// </summary>
    internal static bool IsConcurrencyConflict(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict;
}
