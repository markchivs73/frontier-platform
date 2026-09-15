using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Audit;

/// <summary>
/// One governance change a caller asks to record (ADR-PA30): a mutation made outside an
/// execution, such as a role edit or a model-role mapping decision. The platform is
/// vocabulary-neutral: <see cref="EventType"/> and <see cref="SubjectType"/> are validated
/// snake_case strings, and the consumer owns the catalogue (ADR-E2, ADR-E3a).
/// <see cref="IGovernanceAuditService.AppendAsync"/> derives the record id from this entry's
/// canonical bytes, so an identical retry converges on the record already stored.
/// </summary>
public sealed record GovernanceAuditEntry : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>The chain this entry joins; phase 1 has one, <see cref="GovernanceAuditScopes.Deployment"/>.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("scope")]
    public string Scope { get; init; } = GovernanceAuditScopes.Deployment;

    /// <summary>What happened, in the consumer's snake_case vocabulary, e.g. <c>approver_role_retired</c>.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("event_type")]
    public required string EventType { get; init; }

    /// <summary>The kind of thing changed, in the consumer's snake_case vocabulary, e.g. <c>approver_role</c>.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("subject_type")]
    public required string SubjectType { get; init; }

    /// <summary>The changed thing's identity, e.g. <c>finance-lead</c>.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("subject_id")]
    public required string SubjectId { get; init; }

    /// <summary>The subject's version after the change, when it has one.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("subject_version")]
    public string? SubjectVersion { get; init; }

    /// <summary>
    /// Who directed the change, from the authenticated principal (ADR-E8). Required, never
    /// <c>unknown</c>; a machine origin records an explicit <c>system:*</c> actor.
    /// </summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("actor")]
    public required string Actor { get; init; }

    /// <summary>The actor's UPN, a display convenience only.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("actor_upn")]
    public string? ActorUpn { get; init; }

    /// <summary>Why the change was made. Required.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    /// <summary>When the change was made, as the caller observed it. Informational: the chain is ordered by sequence, never by time.</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName("occurred_at_utc")]
    public required DateTime OccurredAtUtc { get; init; }

    /// <summary>The request's correlation id, when there is one.</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName("correlation_id")]
    public string? CorrelationId { get; init; }

    /// <summary>The engagement the change concerns, when it concerns one.</summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName("engagement_id")]
    public string? EngagementId { get; init; }

    /// <summary>The change itself, as an ADR-E2 envelope. Its content is hashed through RFC 8785 JCS.</summary>
    [JsonPropertyOrder(12)]
    [JsonPropertyName("change")]
    public required TypedPayload Change { get; init; }

    /// <summary>A hash of the subject before the change, when the caller has one.</summary>
    [JsonPropertyOrder(13)]
    [JsonPropertyName("before_hash")]
    public string? BeforeHash { get; init; }

    /// <summary>A hash of the subject after the change, when the caller has one.</summary>
    [JsonPropertyOrder(14)]
    [JsonPropertyName("after_hash")]
    public string? AfterHash { get; init; }

    /// <summary>
    /// For a compensating entry: the <c>record_id</c> of an earlier record whose mutation failed after
    /// its audit landed. The earlier record is never altered; this one is appended after it.
    /// </summary>
    [JsonPropertyOrder(15)]
    [JsonPropertyName("compensates_record_id")]
    public string? CompensatesRecordId { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();
        GovernanceAuditRules.CollectEntryViolations(this, violations);
        GovernanceAuditRules.ThrowIfAny(nameof(GovernanceAuditEntry), violations);
    }
}
