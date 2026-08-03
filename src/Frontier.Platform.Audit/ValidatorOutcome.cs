using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// The result of a Check agent's validation run against a section (doc 05 §3, §7 query 2).
/// No Check agents exist before Stage 6; <see cref="SignedAuditRecord.ValidatorOutcomes"/>
/// is always <c>[]</c> for Stage 5 — this type exists so the consolidator's output shape
/// is complete and query 2 can be demonstrated against an empty list.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6-style round-trip/golden-file tests.")]
public sealed record ValidatorOutcome
{
    /// <summary>The correlation id of the agent invocation whose output this validator checked.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("correlation_id")]
    public required string CorrelationId { get; init; }

    /// <summary>The validator's identity, e.g. <c>"pricing-qa"</c>.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("validator_id")]
    public required string ValidatorId { get; init; }

    /// <summary>The section key the validator ran against.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("target_section_key")]
    public required string TargetSectionKey { get; init; }

    /// <summary>The validator's verdict.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("status")]
    public required ValidatorStatus Status { get; init; }

    /// <summary>Machine-readable finding codes the validator raised, if any.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("finding_codes")]
    public required IReadOnlyList<string> FindingCodes { get; init; }

    /// <summary>UTC timestamp at which the validator ran.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("ran_at_utc")]
    public required DateTime RanAtUtc { get; init; }
}
