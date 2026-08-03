using System.Text.Json.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// The <c>audit-records</c> container's document shape (doc 02 §3, doc 05 §6): wraps a
/// <see cref="SignedAuditRecord"/> with the Cosmos-only <see cref="Id"/> and the
/// <c>/engagement_id</c> partition key.
/// </summary>
internal sealed record SignedAuditRecordDocument
{
    /// <summary>The deterministic document id: <c>{executionId}:audit</c> (doc 05 §6).</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The partition key (doc 05 §6) — mirrors <see cref="SignedAuditRecord.EngagementId"/>.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>The signed audit record.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("record")]
    public required SignedAuditRecord Record { get; init; }

    /// <summary>Wraps <paramref name="record"/> for storage under its deterministic id.</summary>
    internal static SignedAuditRecordDocument FromRecord(SignedAuditRecord record) => new()
    {
        Id = AuditRecordDocumentId.ForExecution(record.ExecutionId),
        EngagementId = record.EngagementId,
        Record = record,
    };
}
