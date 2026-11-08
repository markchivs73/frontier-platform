using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// Who ended a terminated execution, why and when (S13.240, ADR-PA46), copied from the terminated
/// snapshot's <c>terminated_by</c>, <c>termination_reason</c> and <c>terminated_at_utc</c> (ADR-PA26
/// amendment of 2026-11-06) and covered by <c>record_hash</c>. A terminate no person asked for names an
/// explicit <c>system:*</c> actor (ADR-E8).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by round-trip/golden-file tests.")]
public sealed record TerminationRecord
{
    /// <summary>The principal that terminated the run.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("terminated_by")]
    public required string TerminatedBy { get; init; }

    /// <summary>The reason the terminating principal gave.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    /// <summary>When the run was terminated, in UTC.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("terminated_at_utc")]
    public required DateTime TerminatedAtUtc { get; init; }
}
