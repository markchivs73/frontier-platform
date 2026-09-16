using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit;

/// <summary>
/// Why two records in one engagement chain claim the same predecessor (ADR-PA31). Serializes as a
/// snake_case string.
/// </summary>
public sealed class AuditChainForkKind : SmartEnum<AuditChainForkKind>
{
    /// <summary>
    /// The fork predates the concurrency guard: it sits wholly within the records that already
    /// existed when the engagement's chain head was created, or the engagement has no head at all.
    /// This is the S13.106 defect's own footprint, not tampering — nothing was altered, two
    /// concurrent closes simply read the same chain tail before either had written.
    /// </summary>
    public static readonly AuditChainForkKind Legacy = new("legacy_fork");

    /// <summary>
    /// The fork involves a record written after the guard was in force. The platform cannot produce
    /// one — an append now takes the head's ETag — so this is a genuine integrity finding that
    /// warrants investigation.
    /// </summary>
    public static readonly AuditChainForkKind Guarded = new("guarded_fork");

    private AuditChainForkKind(string name)
        : base(name)
    {
    }
}

/// <summary>
/// Two or more records in one engagement chain claiming the same <c>previous_record_hash</c>
/// (ADR-PA31). Reported on its own terms so a fork is never read as a signature mismatch: every
/// record in a fork verifies against its own key perfectly well — it is the chain's shape that is
/// wrong, not any record's content.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; exercised by AuditChainVerifier tests.")]
public sealed record AuditChainFork
{
    /// <summary>The predecessor hash the forked records all claim.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("previous_record_hash")]
    public required string PreviousRecordHash { get; init; }

    /// <summary>The execution ids claiming it, in stored (<c>closed_at_utc</c>) order.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("execution_ids")]
    public required IReadOnlyList<string> ExecutionIds { get; init; }

    /// <summary>Whether the fork predates the guard or was created despite it.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("kind")]
    public required AuditChainForkKind Kind { get; init; }
}
