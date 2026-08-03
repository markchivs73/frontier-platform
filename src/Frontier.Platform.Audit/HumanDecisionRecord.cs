using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit;

/// <summary>
/// A human gate decision projected into the audit timeline (doc 05 §3, §7 query 3) from
/// the execution's <c>ExecutionSnapshot.Decisions</c> (<see cref="HitlDecision"/>).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6-style round-trip/golden-file tests.")]
public sealed record HumanDecisionRecord
{
    /// <summary>The deciding gate's identifier.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("gate_id")]
    public required string GateId { get; init; }

    /// <summary>The approval request this decision answered.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("request_id")]
    public required string RequestId { get; init; }

    /// <summary>The resolved human who decided.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("approver_id")]
    public required string ApproverId { get; init; }

    /// <summary>What was decided.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("kind")]
    public required DecisionKind Kind { get; init; }

    /// <summary>Free-text enrichment from the approver, if any.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    /// <summary>UTC timestamp at which the decision was recorded.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("decided_at_utc")]
    public required DateTime DecidedAtUtc { get; init; }
}
