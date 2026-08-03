using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Hitl;

/// <summary>
/// The <c>approvals</c> container's document shape (doc 02 §3, doc 06 §9): one per
/// gate visit, partitioned by <see cref="EngagementId"/> (ADR-S1). Created
/// <see cref="ApprovalRequestStatus.Pending"/> by <see cref="RequestApprovalActivity"/>;
/// <see cref="EscalateApprovalActivity"/> transitions it to
/// <see cref="ApprovalRequestStatus.Escalated"/> on timeout. <see cref="Decision"/> is
/// embedded once decided — the single ETag-guarded update in the platform (doc 06 §9),
/// written by the future <c>/decide</c> endpoint (S4.7/S9.1).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by ApprovalRequestFactory and CosmosApprovalStore tests.")]
public sealed record ApprovalRequest
{
    /// <summary>Deterministic id: <c>{executionId}:{gateId}:{occurrence}</c> (doc 06 §9).</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The partition key (doc 02 §3, ADR-S1).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>The execution this request belongs to.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("execution_id")]
    public required string ExecutionId { get; init; }

    /// <summary>The <c>HumanGateNode.NodeId</c> this request is for.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("gate_id")]
    public required string GateId { get; init; }

    /// <summary>Mirrors <c>HumanGateNode.GateKind</c> (doc 06 §3).</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("gate_kind")]
    public required GateKind GateKind { get; init; }

    /// <summary>Mirrors <c>HumanGateNode.ApproverRoles</c> (doc 06 §3).</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("approver_roles")]
    public required IReadOnlyList<string> ApproverRoles { get; init; }

    /// <summary>Section key → <c>section-state</c> document ref shown to the approver (doc 06 §9).</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("section_refs")]
    public required IReadOnlyDictionary<string, string> SectionRefs { get; init; }

    /// <summary>The request's current lifecycle state (doc 06 §9).</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("status")]
    public required ApprovalRequestStatus Status { get; init; }

    /// <summary>UTC timestamp at which the gate opened.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("requested_at_utc")]
    public required DateTime RequestedAtUtc { get; init; }

    /// <summary>UTC timestamp at which this request escalates; <see langword="null"/> when <c>HumanGateNode.TimeoutMinutes</c> is <c>0</c> (doc 06 §3, §7).</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName("escalate_at_utc")]
    public DateTime? EscalateAtUtc { get; init; }

    /// <summary>The recorded decision, once <see cref="Status"/> is <see cref="ApprovalRequestStatus.Decided"/> (doc 06 §9).</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName("decision")]
    public HitlDecision? Decision { get; init; }

    /// <summary>Cosmos time-to-live in seconds; <c>-1</c> disables expiry (doc 02 §3, §8).</summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName("ttl")]
    public int Ttl { get; init; } = -1;
}
