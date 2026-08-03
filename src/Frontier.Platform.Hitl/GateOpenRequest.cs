using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Hitl;

/// <summary>
/// Input to <see cref="RequestApprovalActivity"/>: everything needed to open a
/// <c>HumanGateNode</c>'s approval request (doc 06 §4, §9). Built by the
/// orchestrator (Orchestration) from the gate node and current
/// <c>GraphExecutionState</c>; <see cref="RequestedAtUtc"/> is the orchestrator's
/// replay-safe <c>context.CurrentUtcDateTime</c> — activities never call
/// <c>DateTime.Now</c> for values that affect a persisted document's identity or
/// escalation timing.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by ApprovalRequestFactory and RequestApprovalActivity tests.")]
public sealed record GateOpenRequest
{
    /// <summary>The execution this gate belongs to (doc 02 §3, ADR-S1 partition key source).</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("execution_id")]
    public required string ExecutionId { get; init; }

    /// <summary>The engagement this gate belongs to.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>The <c>HumanGateNode.NodeId</c> being opened.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("gate_id")]
    public required string GateId { get; init; }

    /// <summary>Mirrors <c>HumanGateNode.GateKind</c> (doc 06 §3).</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("gate_kind")]
    public required GateKind GateKind { get; init; }

    /// <summary>Mirrors <c>HumanGateNode.ApproverRoles</c> (doc 06 §3).</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("approver_roles")]
    public required IReadOnlyList<string> ApproverRoles { get; init; }

    /// <summary>Section key → <c>section-state</c> document ref shown to the approver (doc 06 §9).</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("section_refs")]
    public required IReadOnlyDictionary<string, string> SectionRefs { get; init; }

    /// <summary>How many times this gate has been visited; <c>0</c> the first time, incremented on re-entry after rollback (doc 06 §4, §13).</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("occurrence")]
    public required int Occurrence { get; init; }

    /// <summary>Mirrors <c>HumanGateNode.TimeoutMinutes</c>; <c>0</c> means no escalation (doc 06 §3).</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("timeout_minutes")]
    public required int TimeoutMinutes { get; init; }

    /// <summary>The orchestrator's replay-safe current time, used as <see cref="ApprovalRequest.RequestedAtUtc"/> and to derive <see cref="ApprovalRequest.EscalateAtUtc"/>.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("requested_at_utc")]
    public required DateTime RequestedAtUtc { get; init; }
}
