using System.Text.Json.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// An engagement's chain head as stored in <c>audit-records</c> (ADR-PA31): id
/// <c>chain-head:{engagementId}</c>, the same <c>/engagement_id</c> partition key as the records it
/// heads, so the append can write both in one transactional batch. Replaced only with If-Match on
/// its ETag. It carries <c>doc_type</c> so record readers can exclude it.
/// </summary>
internal sealed record AuditChainHeadDocument
{
    /// <summary><see cref="AuditRecordDocumentId.ForHead"/>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The partition key — the engagement whose chain this heads.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary><see cref="AuditRecordDocumentId.HeadDocType"/>.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("doc_type")]
    public required string DocType { get; init; }

    /// <summary>How many records the chain holds, the one this head names included.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("sequence")]
    public required long Sequence { get; init; }

    /// <summary>The latest record's hash — the next record's <c>previous_record_hash</c>.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("record_hash")]
    public required string RecordHash { get; init; }

    /// <summary>The execution whose record this head names.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("last_execution_id")]
    public required string LastExecutionId { get; init; }

    /// <summary>
    /// How many records already existed when this head was first created (ADR-PA31's migration):
    /// the records written before the guard existed, which are therefore the ones that could have
    /// forked. Fixed at creation; every later append carries it forward unchanged.
    /// </summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("unguarded_record_count")]
    public required long UnguardedRecordCount { get; init; }

    /// <summary>The head document never expires; it is bookkeeping for a chain that never expires.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("ttl")]
    public int Ttl { get; init; } = SignedAuditRecordDocument.NeverExpires;

    /// <summary>Wraps <paramref name="head"/> for storage.</summary>
    internal static AuditChainHeadDocument FromHead(AuditChainHead head) => new()
    {
        Id = AuditRecordDocumentId.ForHead(head.EngagementId),
        EngagementId = head.EngagementId,
        DocType = AuditRecordDocumentId.HeadDocType,
        Sequence = head.Sequence,
        RecordHash = head.RecordHash,
        LastExecutionId = head.LastExecutionId,
        UnguardedRecordCount = head.UnguardedRecordCount,
    };

    /// <summary>The internal head shape.</summary>
    internal AuditChainHead ToHead() => new(EngagementId, Sequence, RecordHash, LastExecutionId, UnguardedRecordCount);
}
