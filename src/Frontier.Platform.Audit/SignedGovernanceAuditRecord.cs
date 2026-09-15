using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Audit;

/// <summary>
/// A signed, chained record of one governance change (ADR-PA30), stored in
/// <c>governance-audit-records</c>. Fields 1–17 carry the caller's
/// <see cref="GovernanceAuditEntry"/> plus its allocated id and sequence; fields 18–21 chain it
/// to its predecessor in the same scope and sign it. <see cref="RecordHash"/> is SHA-256 over
/// the RFC 8785 JCS form of this record's canonical bytes with <c>record_hash</c> and
/// <c>signature</c> empty, so the change payload's member order cannot move a hash.
/// </summary>
public sealed record SignedGovernanceAuditRecord : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>Deterministic from the entry: SHA-256 over <c>"governance-entry:"</c> and the entry's JCS bytes.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("record_id")]
    public required string RecordId { get; init; }

    /// <summary><see cref="GovernanceAuditEntry.Scope"/>: the chain, and the Cosmos partition key.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    /// <summary>The record's 1-based position in its scope's chain. The chain is ordered by this, never by time.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("sequence")]
    public required long Sequence { get; init; }

    /// <summary><see cref="GovernanceAuditEntry.EventType"/>.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("event_type")]
    public required string EventType { get; init; }

    /// <summary><see cref="GovernanceAuditEntry.SubjectType"/>.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("subject_type")]
    public required string SubjectType { get; init; }

    /// <summary><see cref="GovernanceAuditEntry.SubjectId"/>.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("subject_id")]
    public required string SubjectId { get; init; }

    /// <summary><see cref="GovernanceAuditEntry.SubjectVersion"/>.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("subject_version")]
    public string? SubjectVersion { get; init; }

    /// <summary><see cref="GovernanceAuditEntry.Actor"/>.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("actor")]
    public required string Actor { get; init; }

    /// <summary><see cref="GovernanceAuditEntry.ActorUpn"/>.</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName("actor_upn")]
    public string? ActorUpn { get; init; }

    /// <summary><see cref="GovernanceAuditEntry.Reason"/>.</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    /// <summary><see cref="GovernanceAuditEntry.OccurredAtUtc"/>.</summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName("occurred_at_utc")]
    public required DateTime OccurredAtUtc { get; init; }

    /// <summary><see cref="GovernanceAuditEntry.CorrelationId"/>.</summary>
    [JsonPropertyOrder(12)]
    [JsonPropertyName("correlation_id")]
    public string? CorrelationId { get; init; }

    /// <summary><see cref="GovernanceAuditEntry.EngagementId"/>.</summary>
    [JsonPropertyOrder(13)]
    [JsonPropertyName("engagement_id")]
    public string? EngagementId { get; init; }

    /// <summary><see cref="GovernanceAuditEntry.Change"/>.</summary>
    [JsonPropertyOrder(14)]
    [JsonPropertyName("change")]
    public required TypedPayload Change { get; init; }

    /// <summary><see cref="GovernanceAuditEntry.BeforeHash"/>.</summary>
    [JsonPropertyOrder(15)]
    [JsonPropertyName("before_hash")]
    public string? BeforeHash { get; init; }

    /// <summary><see cref="GovernanceAuditEntry.AfterHash"/>.</summary>
    [JsonPropertyOrder(16)]
    [JsonPropertyName("after_hash")]
    public string? AfterHash { get; init; }

    /// <summary><see cref="GovernanceAuditEntry.CompensatesRecordId"/>.</summary>
    [JsonPropertyOrder(17)]
    [JsonPropertyName("compensates_record_id")]
    public string? CompensatesRecordId { get; init; }

    /// <summary>The predecessor's <see cref="RecordHash"/>; for sequence 1, SHA-256 of <c>"governance:" + scope</c>.</summary>
    [JsonPropertyOrder(18)]
    [JsonPropertyName("previous_record_hash")]
    public required string PreviousRecordHash { get; init; }

    /// <summary>SHA-256 of the record's JCS bytes with this field and <see cref="Signature"/> empty. Covers <see cref="SigningKeyId"/>.</summary>
    [JsonPropertyOrder(19)]
    [JsonPropertyName("record_hash")]
    public required string RecordHash { get; init; }

    /// <summary>HMAC-SHA256(<see cref="RecordHash"/>, signing key) (RFC 2104).</summary>
    [JsonPropertyOrder(20)]
    [JsonPropertyName("signature")]
    public required string Signature { get; init; }

    /// <summary>The key version that signed the record; verification resolves this version, never the current one (ADR-PA22).</summary>
    [JsonPropertyOrder(21)]
    [JsonPropertyName("signing_key_id")]
    public required string SigningKeyId { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();
        GovernanceAuditRules.CollectEntryViolations(GovernanceAuditHasher.ToEntry(this), violations);

        if (string.IsNullOrWhiteSpace(RecordId))
        {
            violations.Add("record_id must not be empty.");
        }

        if (Sequence < 1)
        {
            violations.Add("sequence must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(PreviousRecordHash) || string.IsNullOrWhiteSpace(RecordHash) ||
            string.IsNullOrWhiteSpace(Signature) || string.IsNullOrWhiteSpace(SigningKeyId))
        {
            violations.Add("previous_record_hash, record_hash, signature, and signing_key_id must all be present.");
        }

        GovernanceAuditRules.ThrowIfAny(nameof(SignedGovernanceAuditRecord), violations);
    }
}
